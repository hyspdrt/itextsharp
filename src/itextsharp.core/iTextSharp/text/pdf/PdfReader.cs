/*
	This file is part of the iText (R) project.
	Copyright (c) 1998-2019 iText Group NV
	Authors: iText Software.

	This program is free software; you can redistribute it and/or modify
	it under the terms of the GNU Affero General Public License version 3
	as published by the Free Software Foundation with the addition of the
	following permission added to Section 15 as permitted in Section 7(a):
	FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY
	ITEXT GROUP. ITEXT GROUP DISCLAIMS THE WARRANTY OF NON INFRINGEMENT
	OF THIRD PARTY RIGHTS
	
	This program is distributed in the hope that it will be useful, but
	WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY
	or FITNESS FOR A PARTICULAR PURPOSE.
	See the GNU Affero General Public License for more details.
	You should have received a copy of the GNU Affero General Public License
	along with this program; if not, see http://www.gnu.org/licenses or write to
	the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor,
	Boston, MA, 02110-1301 USA, or download the license from the following URL:
	http://itextpdf.com/terms-of-use/
	
	The interactive user interfaces in modified source and object code versions
	of this program must display Appropriate Legal Notices, as required under
	Section 5 of the GNU Affero General Public License.
	
	In accordance with Section 7(b) of the GNU Affero General Public License,
	a covered work must retain the producer line in every PDF that is created
	or manipulated using iText.
	
	You can be released from the requirements of the license by purchasing
	a commercial license. Buying such a license is mandatory as soon as you
	develop commercial activities involving the iText software without
	disclosing the source code of your own applications.
	These activities include: offering paid services to customers as an ASP,
	serving PDFs on the fly in a web application, shipping iText with a closed
	source product.
	
	For more information, please contact iText Software Corp. at this
	address: sales@itextpdf.com
 */
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using iTextSharp.text.exceptions;
using iTextSharp.text.log;
using iTextSharp.text.pdf.intern;
using iTextSharp.text.pdf.interfaces;
using System.util;
using System.util.zlib;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.X509;
using iTextSharp.text.error_messages;
using iTextSharp.text.io;
using System.util.collections;

namespace iTextSharp.text.pdf {
	/** Reads a PDF document.
	* @author Paulo Soares
	* @author Kazuya Ujihara
	*/
	public class PdfReader : IPdfViewerPreferences, IDisposable {

		/**
		 * The iText developers are not responsible if you decide to change the
		 * value of this static parameter.
		 * @since 5.0.2
		 */
		public static bool unethicalreading = false;

		public static bool debugmode = false;
		private static ILogger LOGGER = LoggerFactory.GetLogger(typeof(PdfReader));

		static PdfName[] pageInhCandidates = {
			PdfName.MEDIABOX, PdfName.ROTATE, PdfName.RESOURCES, PdfName.CROPBOX
		};

		static byte[] endstream = PdfEncodings.ConvertToBytes("endstream", null);
		static byte[] endobj = PdfEncodings.ConvertToBytes("endobj", null);
		protected internal PRTokeniser tokens;
		// Each xref pair is a position
		// type 0 -> -1, 0
		// type 1 -> offset, 0
		// type 2 -> index, obj num
		protected internal long[] xref;
		protected internal Dictionary<int, IntHashtable> objStmMark;
		protected internal LongHashtable objStmToOffset;
		protected internal bool newXrefType;
		protected List<PdfObject> xrefObj;
		PdfDictionary rootPages;
		protected internal PdfDictionary trailer;
		protected internal PdfDictionary catalog;
		protected internal PageRefs pageRefs;
		protected internal PRAcroForm acroForm = null;
		protected internal bool acroFormParsed = false;
		protected internal bool encrypted = false;
		protected internal bool rebuilt = false;
		protected internal int freeXref;
		protected internal bool tampered = false;
		protected internal long lastXref;
		protected internal long eofPos;
		protected internal char pdfVersion;
		protected internal PdfEncryption decrypt;
		protected internal byte[] password = null; //added by ujihara for decryption
		protected ICipherParameters certificateKey = null; //added by Aiken Sam for certificate decryption
		protected X509Certificate certificate = null; //added by Aiken Sam for certificate decryption
		private bool ownerPasswordUsed;
		protected internal List<PdfString> strings = new List<PdfString>();
		protected internal bool sharedStreams = true;
		protected internal bool consolidateNamedDestinations = false;
		protected bool remoteToLocalNamedDestinations = false;
		protected internal int rValue;
		protected internal long pValue;
		private int objNum;
		private int objGen;
		private long fileLength;
		private bool hybridXref;
		private int lastXrefPartial = -1;
		private bool partial;
		private PRIndirectReference cryptoRef;
		private PdfViewerPreferencesImp viewerPreferences = new PdfViewerPreferencesImp();
		private bool encryptionError;

		/**
		* Handler which will be used for decompression of pdf streams.
		*/
		internal MemoryLimitsAwareHandler memoryLimitsAwareHandler = null;

		/**
		* Holds value of property appendable.
		*/
		private bool appendable;

		protected static ICounter COUNTER = CounterFactory.GetCounter(typeof(PdfReader));
		protected virtual ICounter GetCounter() {
			return COUNTER;
		}

		internal MemoryLimitsAwareHandler GetMemoryLimitsAwareHandler() {
			return memoryLimitsAwareHandler;
		}

		protected internal PdfReader() {
		}

		/**
		 * Constructs a new PdfReader.  This is the master constructor.
		 * @param byteSource source of bytes for the reader
		 * @param partialRead if true, the reader is opened in partial mode (PDF is parsed on demand), if false, the entire PDF is parsed into memory as the reader opens
		 * @param ownerPassword the password or null if no password is required
		 * @param certificate the certificate or null if no certificate is required
		 * @param certificateKey the key or null if no certificate key is required
		 * @param certificateKeyProvider the name of the key provider, or null if no key is required
		 * @param closeSourceOnConstructorError if true, the byteSource will be closed if there is an error during construction of this reader
		 */
		private PdfReader(IRandomAccessSource byteSource, bool partialRead, byte[] ownerPassword, X509Certificate certificate, ICipherParameters certificateKey, bool closeSourceOnConstructorError) :
			this(new ReaderProperties().SetCertificate(certificate).SetCertificateKey(certificateKey).SetOwnerPassword(ownerPassword).SetPartialRead(partialRead).SetCloseSourceOnconstructorError(closeSourceOnConstructorError), byteSource) {
		}

		/**
		* Constructs a new PdfReader.  This is the master constructor.
		* @param byteSource source of bytes for the reader
		* @param properties the properties which will be used to create the reader
		*/
		private PdfReader(ReaderProperties properties, IRandomAccessSource byteSource) {
			this.certificate = properties.certificate;
			this.certificateKey = properties.certificateKey;
			this.password = properties.ownerPassword;
			this.partial = properties.partialRead;
			this.memoryLimitsAwareHandler = properties.memoryLimitsAwareHandler;
			try {

				tokens = GetOffsetTokeniser(byteSource);

				if (properties.partialRead)
					ReadPdfPartial();
				else
					ReadPdf();
			} catch (IOException e) {
				if (properties.closeSourceOnconstructorError)
					byteSource.Close();
				throw e;
			}
			GetCounter().Read(fileLength);
		}

		/** Reads and parses a PDF document.
		* @param filename the file name of the document
		* @throws IOException on error
		*/
		public PdfReader(String filename) : this(filename, null) {
		}

		/**
		* Reads and parses a PDF document.
		* @param filename the file name of the document
		* @param properties the properties which will be used to create the reader
		* @throws IOException on error
		*/
		public PdfReader(ReaderProperties properties, String filename) : this(
			properties,
			new RandomAccessSourceFactory()
			.SetForceRead(false)
			.CreateBestSource(filename)) {
		}

		/** Reads and parses a PDF document.
		* @param filename the file name of the document
		* @param ownerPassword the password to read the document
		* @throws IOException on error
		*/
		public PdfReader(String filename, byte[] ownerPassword) :
			this(new ReaderProperties().SetOwnerPassword(ownerPassword), filename) {
		}

		/** Reads and parses a PDF document.
		 * @param filename the file name of the document
		 * @param ownerPassword the password to read the document
		 * @throws IOException on error
		 */
		public PdfReader(String filename, byte[] ownerPassword, bool partial) : this(
			new ReaderProperties().SetPartialRead(partial).SetOwnerPassword(ownerPassword),
			new RandomAccessSourceFactory()
			.SetForceRead(false)
			.CreateBestSource(filename)) {
		}

		/** Reads and parses a PDF document.
		* @param pdfIn the byte array with the document
		* @throws IOException on error
		*/
		public PdfReader(byte[] pdfIn) : this(new ReaderProperties(), new RandomAccessSourceFactory().CreateSource(pdfIn)) {
		}

		/** Reads and parses a PDF document.
		* @param pdfIn the byte array with the document
		* @param ownerPassword the password to read the document
		* @throws IOException on error
		*/
		public PdfReader(byte[] pdfIn, byte[] ownerPassword) : this(
			new ReaderProperties().SetOwnerPassword(ownerPassword),
			new RandomAccessSourceFactory().CreateSource(pdfIn)) {
		}

		/** Reads and parses a PDF document.
		* @param filename the file name of the document
		* @param certificate the certificate to read the document
		* @param certificateKey the private key of the certificate
		* @param certificateKeyProvider the security provider for certificateKey
		* @throws IOException on error
		*/
		public PdfReader(String filename, X509Certificate certificate, ICipherParameters certificateKey) : this(
			new ReaderProperties().SetCertificate(certificate).SetCertificateKey(certificateKey),
			new RandomAccessSourceFactory()
			.SetForceRead(false)
			.CreateBestSource(filename)) {
		}

		/** Reads and parses a PDF document.
			* @param url the Uri of the document
			* @throws IOException on error
			*/
		public PdfReader(Uri url) : this(new ReaderProperties(), new RandomAccessSourceFactory().CreateSource(url)) {
		}

		/** Reads and parses a PDF document.
		* @param url the Uri of the document
		* @param ownerPassword the password to read the document
		* @throws IOException on error
		*/
		public PdfReader(Uri url, byte[] ownerPassword) : this(
			new ReaderProperties().SetOwnerPassword(ownerPassword),
			new RandomAccessSourceFactory().CreateSource(url)) {
		}

		/**
		* Reads and parses a PDF document.
		* @param is the <CODE>InputStream</CODE> containing the document. The stream is read to the
		* end but is not closed
		* @param ownerPassword the password to read the document
		* @throws IOException on error
		*/
		public PdfReader(Stream isp, byte[] ownerPassword) : this(
			new ReaderProperties().SetOwnerPassword(ownerPassword).SetCloseSourceOnconstructorError(false),
			new RandomAccessSourceFactory().CreateSource(isp)) {
		}

		/**
		* Reads and parses a PDF document.
		* @param properties the properties which will be used to create the reader
		* @param isp the <CODE>InputStream</CODE> containing the document. The stream is read to the
		* end but is not closed
		* @throws IOException on error
		*/
		public PdfReader(ReaderProperties properties, Stream isp) : this(properties, new RandomAccessSourceFactory().CreateSource(isp)) {
		}

		/**
		* Reads and parses a PDF document.
		* @param isp the <CODE>InputStream</CODE> containing the document. The stream is read to the
		* end but is not closed
		* @throws IOException on error
		*/
		public PdfReader(Stream isp) : this(new ReaderProperties().SetCloseSourceOnconstructorError(false), new RandomAccessSourceFactory().CreateSource(isp)) {
		}

		/**
		 * Reads and parses a pdf document. Contrary to the other constructors only the xref is read
		 * into memory. The reader is said to be working in "partial" mode as only parts of the pdf
		 * are read as needed.
		 * @param raf the document location
		 * @param ownerPassword the password or <CODE>null</CODE> for no password
		 * @throws IOException on error
		 */
		public PdfReader(RandomAccessFileOrArray raf, byte[] ownerPassword) :
			this(new ReaderProperties().SetOwnerPassword(ownerPassword).SetPartialRead(true).SetCloseSourceOnconstructorError(false), raf.GetByteSource()) {
		}

		/**
		 * Reads and parses a pdf document.
		 * @param raf the document location
		 * @param ownerPassword the password or <CODE>null</CODE> for no password
		 * @param partial indicates if the reader needs to read the document only partially. See {@link PdfReader#PdfReader(RandomAccessFileOrArray, byte[])}
		 * @throws IOException on error
		 */
		public PdfReader(RandomAccessFileOrArray raf, byte[] ownerPassword, bool partial) :
			this(new ReaderProperties().SetPartialRead(partial).SetOwnerPassword(ownerPassword).SetCloseSourceOnconstructorError(false),
					raf.GetByteSource()) {
		}

		/** Creates an independent duplicate.
		* @param reader the <CODE>PdfReader</CODE> to duplicate
		*/
		public PdfReader(PdfReader reader) {
			this.appendable = reader.appendable;
			this.consolidateNamedDestinations = reader.consolidateNamedDestinations;
			this.encrypted = reader.encrypted;
			this.rebuilt = reader.rebuilt;
			this.sharedStreams = reader.sharedStreams;
			this.tampered = reader.tampered;
			this.password = reader.password;
			this.pdfVersion = reader.pdfVersion;
			this.eofPos = reader.eofPos;
			this.freeXref = reader.freeXref;
			this.lastXref = reader.lastXref;
			this.newXrefType = reader.newXrefType;
			this.tokens = new PRTokeniser(reader.tokens.SafeFile);
			if (reader.decrypt != null)
				this.decrypt = new PdfEncryption(reader.decrypt);
			this.pValue = reader.pValue;
			this.rValue = reader.rValue;
			this.xrefObj = new List<PdfObject>(reader.xrefObj);
			for (var k = 0; k < reader.xrefObj.Count; ++k) {
				this.xrefObj[k] = DuplicatePdfObject(reader.xrefObj[k], this);
			}
			this.pageRefs = new PageRefs(reader.pageRefs, this);
			this.trailer = (PdfDictionary)DuplicatePdfObject(reader.trailer, this);
			this.catalog = trailer.GetAsDict(PdfName.ROOT);
			this.rootPages = catalog.GetAsDict(PdfName.PAGES);
			this.fileLength = reader.fileLength;
			this.partial = reader.partial;
			this.hybridXref = reader.hybridXref;
			this.objStmToOffset = reader.objStmToOffset;
			this.xref = reader.xref;
			this.cryptoRef = (PRIndirectReference)DuplicatePdfObject(reader.cryptoRef, this);
			this.ownerPasswordUsed = reader.ownerPasswordUsed;
		}

		// Explicit static constructor to tell C# compiler
		// not to mark type as beforefieldinit. It prevents
		// problems with Logger initialization.
		static PdfReader() {
		}

		/**
		 * Utility method that checks the provided byte source to see if it has junk bytes at the beginning.  If junk bytes
		 * are found, construct a tokeniser that ignores the junk.  Otherwise, construct a tokeniser for the byte source as it is
		 * @param byteSource the source to check
		 * @return a tokeniser that is guaranteed to start at the PDF header
		 * @throws IOException if there is a problem reading the byte source
		 */
		private static PRTokeniser GetOffsetTokeniser(IRandomAccessSource byteSource) {
			var tok = new PRTokeniser(new RandomAccessFileOrArray(byteSource));
			var offset = tok.GetHeaderOffset();
			if (offset != 0) {
				IRandomAccessSource offsetSource = new WindowRandomAccessSource(byteSource, offset);
				tok = new PRTokeniser(new RandomAccessFileOrArray(offsetSource));
			}
			return tok;
		}

		/** Gets a new file instance of the original PDF
		* document.
		* @return a new file instance of the original PDF document
		*/
		virtual public RandomAccessFileOrArray SafeFile {
			get {
				return tokens.SafeFile;
			}
		}

		virtual protected internal PdfReaderInstance GetPdfReaderInstance(PdfWriter writer) {
			return new PdfReaderInstance(this, writer);
		}

		/** Gets the number of pages in the document.
		* @return the number of pages in the document
		*/
		virtual public int NumberOfPages {
			get {
				return pageRefs.Size;
			}
		}

		/** Returns the document's catalog. This dictionary is not a copy,
		* any changes will be reflected in the catalog.
		* @return the document's catalog
		*/
		virtual public PdfDictionary Catalog {
			get {
				return catalog;
			}
		}

		/** Returns the document's acroform, if it has one.
		* @return the document's acroform
		*/
		virtual public PRAcroForm AcroForm {
			get {
				if (!acroFormParsed) {
					acroFormParsed = true;
					var form = catalog.Get(PdfName.ACROFORM);
					if (form != null) {
						try {
							acroForm = new PRAcroForm(this);
							acroForm.ReadAcroForm((PdfDictionary)GetPdfObject(form));
						} catch {
							acroForm = null;
						}
					}
				}
				return acroForm;
			}
		}

		/**
		* Gets the page rotation. This value can be 0, 90, 180 or 270.
		* @param index the page number. The first page is 1
		* @return the page rotation
		*/
		virtual public int GetPageRotation(int index) {
			return GetPageRotation(pageRefs.GetPageNRelease(index));
		}

		internal int GetPageRotation(PdfDictionary page) {
			var rotate = page.GetAsNumber(PdfName.ROTATE);
			if (rotate == null)
				return 0;
			else {
				var n = rotate.IntValue;
				n %= 360;
				return n < 0 ? n + 360 : n;
			}
		}
		/** Gets the page size, taking rotation into account. This
		* is a <CODE>Rectangle</CODE> with the value of the /MediaBox and the /Rotate key.
		* @param index the page number. The first page is 1
		* @return a <CODE>Rectangle</CODE>
		*/
		virtual public Rectangle GetPageSizeWithRotation(int index) {
			return GetPageSizeWithRotation(pageRefs.GetPageNRelease(index));
		}

		/**
		* Gets the rotated page from a page dictionary.
		* @param page the page dictionary
		* @return the rotated page
		*/
		virtual public Rectangle GetPageSizeWithRotation(PdfDictionary page) {
			var rect = GetPageSize(page);
			var rotation = GetPageRotation(page);
			while (rotation > 0) {
				rect = rect.Rotate();
				rotation -= 90;
			}
			return rect;
		}

		/** Gets the page size without taking rotation into account. This
		* is the value of the /MediaBox key.
		* @param index the page number. The first page is 1
		* @return the page size
		*/
		virtual public Rectangle GetPageSize(int index) {
			return GetPageSize(pageRefs.GetPageNRelease(index));
		}

		/**
		* Gets the page from a page dictionary
		* @param page the page dictionary
		* @return the page
		*/
		virtual public Rectangle GetPageSize(PdfDictionary page) {
			var mediaBox = page.GetAsArray(PdfName.MEDIABOX);
			return GetNormalizedRectangle(mediaBox);
		}

		/** Gets the crop box without taking rotation into account. This
		* is the value of the /CropBox key. The crop box is the part
		* of the document to be displayed or printed. It usually is the same
		* as the media box but may be smaller. If the page doesn't have a crop
		* box the page size will be returned.
		* @param index the page number. The first page is 1
		* @return the crop box
		*/
		virtual public Rectangle GetCropBox(int index) {
			var page = pageRefs.GetPageNRelease(index);
			var cropBox = (PdfArray)GetPdfObjectRelease(page.Get(PdfName.CROPBOX));
			if (cropBox == null)
				return GetPageSize(page);
			return GetNormalizedRectangle(cropBox);
		}

		/** Gets the box size. Allowed names are: "crop", "trim", "art", "bleed" and "media".
		* @param index the page number. The first page is 1
		* @param boxName the box name
		* @return the box rectangle or null
		*/
		virtual public Rectangle GetBoxSize(int index, String boxName) {
			var page = pageRefs.GetPageNRelease(index);
			PdfArray box = null;
			if (boxName.Equals("trim"))
				box = (PdfArray)GetPdfObjectRelease(page.Get(PdfName.TRIMBOX));
			else if (boxName.Equals("art"))
				box = (PdfArray)GetPdfObjectRelease(page.Get(PdfName.ARTBOX));
			else if (boxName.Equals("bleed"))
				box = (PdfArray)GetPdfObjectRelease(page.Get(PdfName.BLEEDBOX));
			else if (boxName.Equals("crop"))
				box = (PdfArray)GetPdfObjectRelease(page.Get(PdfName.CROPBOX));
			else if (boxName.Equals("media"))
				box = (PdfArray)GetPdfObjectRelease(page.Get(PdfName.MEDIABOX));
			if (box == null)
				return null;
			return GetNormalizedRectangle(box);
		}

		/** Returns the content of the document information dictionary as a <CODE>Hashtable</CODE>
		* of <CODE>String</CODE>.
		* @return content of the document information dictionary
		*/
		virtual public Dictionary<string, string> Info {
			get {
				var map = new Dictionary<string, string>();
				var info = trailer.GetAsDict(PdfName.INFO);
				if (info == null)
					return map;
				foreach (var key in info.Keys) {
					var obj = GetPdfObject(info.Get(key));
					if (obj == null)
						continue;
					var value = obj.ToString();
					switch (obj.Type) {
						case PdfObject.STRING: {
							value = ((PdfString)obj).ToUnicodeString();
							break;
						}
						case PdfObject.NAME: {
							value = PdfName.DecodeName(value);
							break;
						}
					}
					map[PdfName.DecodeName(key.ToString())] = value;
				}
				return map;
			}
		}

		/** Normalizes a <CODE>Rectangle</CODE> so that llx and lly are smaller than urx and ury.
		* @param box the original rectangle
		* @return a normalized <CODE>Rectangle</CODE>
		*/
		public static Rectangle GetNormalizedRectangle(PdfArray box) {
			var llx = ((PdfNumber)GetPdfObjectRelease(box.GetPdfObject(0))).FloatValue;
			var lly = ((PdfNumber)GetPdfObjectRelease(box.GetPdfObject(1))).FloatValue;
			var urx = ((PdfNumber)GetPdfObjectRelease(box.GetPdfObject(2))).FloatValue;
			var ury = ((PdfNumber)GetPdfObjectRelease(box.GetPdfObject(3))).FloatValue;
			return new Rectangle(Math.Min(llx, urx), Math.Min(lly, ury),
			Math.Max(llx, urx), Math.Max(lly, ury));
		}


		/**
		 * Checks if the PDF is a tagged PDF.
		 */
		virtual public bool IsTagged() {
			var markInfo = catalog.GetAsDict(PdfName.MARKINFO);
			if (markInfo == null)
				return false;
			if (PdfBoolean.PDFTRUE.Equals(markInfo.GetAsBoolean(PdfName.MARKED))) {
				return catalog.GetAsDict(PdfName.STRUCTTREEROOT) != null;
			} else {
				return false;
			}
		}

		/**
		 * Parses the entire PDF
		 */
		protected internal virtual void ReadPdf() {
			fileLength = tokens.File.Length;
			pdfVersion = tokens.CheckPdfHeader();
			if (null == memoryLimitsAwareHandler) {
				memoryLimitsAwareHandler = new MemoryLimitsAwareHandler(fileLength);
			}
			try {
				ReadXref();
			} catch (Exception e) {
				try {
					rebuilt = true;
					RebuildXref();
					lastXref = -1;
				} catch (Exception ne) {
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("rebuild.failed.1.original.message.2", ne.Message, e.Message));
				}
			}
			try {
				ReadDocObj();
			} catch (Exception ne) {
				if (ne is BadPasswordException)
					throw new BadPasswordException(ne.Message);
				if (rebuilt || encryptionError)
					throw new InvalidPdfException(ne.Message);
				rebuilt = true;
				encrypted = false;
				try {
					RebuildXref();
					lastXref = -1;
					ReadDocObj();
				} catch (Exception ne2) {
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("rebuild.failed.1.original.message.2", ne2.Message, ne.Message));
				}
			}

			strings.Clear();
			ReadPages();
			//EliminateSharedStreams();
			RemoveUnusedObjects();
		}

		virtual protected internal void ReadPdfPartial() {
			fileLength = tokens.File.Length;
			pdfVersion = tokens.CheckPdfHeader();
			if (null == memoryLimitsAwareHandler) {
				memoryLimitsAwareHandler = new MemoryLimitsAwareHandler(fileLength);
			}
			try {
				ReadXref();
			} catch (Exception e) {
				try {
					rebuilt = true;
					RebuildXref();
					lastXref = -1;
				} catch (Exception ne) {
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage(
							"rebuild.failed.1.original.message.2", ne.Message, e.Message), ne);
				}
			}
			ReadDocObjPartial();
			ReadPages();
		}

		private bool EqualsArray(byte[] ar1, byte[] ar2, int size) {
			for (var k = 0; k < size; ++k) {
				if (ar1[k] != ar2[k])
					return false;
			}
			return true;
		}

		/**
		* @throws IOException
		*/
		private void ReadDecryptedDocObj() {
			if (encrypted)
				return;
			var encDic = trailer.Get(PdfName.ENCRYPT);
			if (encDic == null || encDic.ToString().Equals("null"))
				return;
			encryptionError = true;
			byte[] encryptionKey = null;

			encrypted = true;
			var enc = (PdfDictionary)GetPdfObject(encDic);
			//This string of condidions is to determine whether or not the authevent for this PDF is EFOPEN
			//If it is, we return since the attachments of the PDF are what are encrypted, not the PDF itself.  
			//Without this check we run into a bad password exception when trying to open documents that have an
			//auth event type of EFOPEN. 
			var cfDict = enc.GetAsDict(PdfName.CF);
			if (cfDict != null) {
				var stdCFDict = cfDict.GetAsDict(PdfName.STDCF);
				if (stdCFDict != null) {
					var authEvent = stdCFDict.GetAsName(PdfName.AUTHEVENT);
					if (authEvent != null) {
						//Return only if the event is EFOPEN and there is no password so that 
						//attachments that are encrypted can still be opened.
						if (authEvent.CompareTo(PdfName.EFOPEN) == 0 && !this.ownerPasswordUsed)
							return;
					}
				}
			}

			String s;
			PdfObject o;

			var documentIDs = trailer.GetAsArray(PdfName.ID);
			byte[] documentID = null;
			if (documentIDs != null) {
				o = documentIDs.GetPdfObject(0);
				strings.Remove((PdfString)o);
				s = o.ToString();
				documentID = DocWriter.GetISOBytes(s);
				if (documentIDs.Size > 1)
					strings.Remove((PdfString)documentIDs.GetPdfObject(1));
			}
			// just in case we have a broken producer
			if (documentID == null)
				documentID = new byte[0];

			byte[] uValue = null;
			byte[] oValue = null;
			var cryptoMode = PdfWriter.STANDARD_ENCRYPTION_40;
			var lengthValue = 0;

			var filter = GetPdfObjectRelease(enc.Get(PdfName.FILTER));

			if (filter.Equals(PdfName.STANDARD)) {
				s = enc.Get(PdfName.U).ToString();
				strings.Remove((PdfString)enc.Get(PdfName.U));
				uValue = DocWriter.GetISOBytes(s);
				s = enc.Get(PdfName.O).ToString();
				strings.Remove((PdfString)enc.Get(PdfName.O));
				oValue = DocWriter.GetISOBytes(s);
				if (enc.Contains(PdfName.OE))
					strings.Remove((PdfString)enc.Get(PdfName.OE));
				if (enc.Contains(PdfName.UE))
					strings.Remove((PdfString)enc.Get(PdfName.UE));
				if (enc.Contains(PdfName.PERMS))
					strings.Remove((PdfString)enc.Get(PdfName.PERMS));

				o = enc.Get(PdfName.P);
				if (!o.IsNumber())
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.p.value"));
				pValue = ((PdfNumber)o).LongValue;

				o = enc.Get(PdfName.R);
				if (!o.IsNumber())
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.r.value"));
				rValue = ((PdfNumber)o).IntValue;

				switch (rValue) {
					case 2:
						cryptoMode = PdfWriter.STANDARD_ENCRYPTION_40;
						break;
					case 3:
						o = enc.Get(PdfName.LENGTH);
						if (!o.IsNumber())
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.length.value"));
						lengthValue = ((PdfNumber)o).IntValue;
						if (lengthValue > 128 || lengthValue < 40 || lengthValue % 8 != 0)
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.length.value"));
						cryptoMode = PdfWriter.STANDARD_ENCRYPTION_128;
						break;
					case 4:
						var dic = (PdfDictionary)enc.Get(PdfName.CF);
						if (dic == null)
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("cf.not.found.encryption"));
						dic = (PdfDictionary)dic.Get(PdfName.STDCF);
						if (dic == null)
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("stdcf.not.found.encryption"));
						if (PdfName.V2.Equals(dic.Get(PdfName.CFM)))
							cryptoMode = PdfWriter.STANDARD_ENCRYPTION_128;
						else if (PdfName.AESV2.Equals(dic.Get(PdfName.CFM)))
							cryptoMode = PdfWriter.ENCRYPTION_AES_128;
						else
							throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("no.compatible.encryption.found"));
						var em = enc.Get(PdfName.ENCRYPTMETADATA);
						if (em != null && em.ToString().Equals("false"))
							cryptoMode |= PdfWriter.DO_NOT_ENCRYPT_METADATA;
						break;
					case 5:
						cryptoMode = PdfWriter.ENCRYPTION_AES_256;
						var em5 = enc.Get(PdfName.ENCRYPTMETADATA);
						if (em5 != null && em5.ToString().Equals("false"))
							cryptoMode |= PdfWriter.DO_NOT_ENCRYPT_METADATA;
						break;
					default:
						throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("unknown.encryption.type.r.eq.1", rValue));
				}
			} else if (filter.Equals(PdfName.PUBSEC)) {
				var foundRecipient = false;
				byte[] envelopedData = null;
				PdfArray recipients = null;

				o = enc.Get(PdfName.V);
				if (!o.IsNumber())
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.v.value"));
				var vValue = ((PdfNumber)o).IntValue;
				switch (vValue) {
					case 1:
						cryptoMode = PdfWriter.STANDARD_ENCRYPTION_40;
						lengthValue = 40;
						recipients = (PdfArray)enc.Get(PdfName.RECIPIENTS);
						break;
					case 2:
						o = enc.Get(PdfName.LENGTH);
						if (!o.IsNumber())
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.length.value"));
						lengthValue = ((PdfNumber)o).IntValue;
						if (lengthValue > 128 || lengthValue < 40 || lengthValue % 8 != 0)
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.length.value"));
						cryptoMode = PdfWriter.STANDARD_ENCRYPTION_128;
						recipients = (PdfArray)enc.Get(PdfName.RECIPIENTS);
						break;
					case 4:
					case 5:
						var dic = (PdfDictionary)enc.Get(PdfName.CF);
						if (dic == null)
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("cf.not.found.encryption"));
						dic = (PdfDictionary)dic.Get(PdfName.DEFAULTCRYPTFILTER);
						if (dic == null)
							throw new InvalidPdfException(MessageLocalization.GetComposedMessage("defaultcryptfilter.not.found.encryption"));
						if (PdfName.V2.Equals(dic.Get(PdfName.CFM))) {
							cryptoMode = PdfWriter.STANDARD_ENCRYPTION_128;
							lengthValue = 128;
						} else if (PdfName.AESV2.Equals(dic.Get(PdfName.CFM))) {
							cryptoMode = PdfWriter.ENCRYPTION_AES_128;
							lengthValue = 128;
						} else if (PdfName.AESV3.Equals(dic.Get(PdfName.CFM))) {
							cryptoMode = PdfWriter.ENCRYPTION_AES_256;
							lengthValue = 256;
						} else
							throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("no.compatible.encryption.found"));
						var em = dic.Get(PdfName.ENCRYPTMETADATA);
						if (em != null && em.ToString().Equals("false"))
							cryptoMode |= PdfWriter.DO_NOT_ENCRYPT_METADATA;

						recipients = (PdfArray)dic.Get(PdfName.RECIPIENTS);
						break;
					default:
						throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("unknown.encryption.type.v.eq.1", vValue));
				}
				for (var i = 0; i < recipients.Size; i++) {
					var recipient = recipients.GetPdfObject(i);
					if (recipient is PdfString)
						strings.Remove((PdfString)recipient);

					CmsEnvelopedData data = null;
					data = new CmsEnvelopedData(recipient.GetBytes());

					foreach (RecipientInformation recipientInfo in data.GetRecipientInfos().GetRecipients()) {
						if (recipientInfo.RecipientID.Match(certificate) && !foundRecipient) {
							envelopedData = recipientInfo.GetContent(certificateKey);
							foundRecipient = true;
						}
					}
				}

				if (!foundRecipient || envelopedData == null) {
					throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("bad.certificate.and.key"));
				}

				IDigest sh;
				if ((cryptoMode & PdfWriter.ENCRYPTION_MASK) == PdfWriter.ENCRYPTION_AES_256)
					sh = DigestUtilities.GetDigest("SHA-256");
				else
					sh = DigestUtilities.GetDigest("SHA-1");
				sh.BlockUpdate(envelopedData, 0, 20);
				for (var i = 0; i < recipients.Size; i++) {
					var encodedRecipient = recipients.GetPdfObject(i).GetBytes();
					sh.BlockUpdate(encodedRecipient, 0, encodedRecipient.Length);
				}
				if ((cryptoMode & PdfWriter.DO_NOT_ENCRYPT_METADATA) != 0)
					sh.BlockUpdate(PdfEncryption.metadataPad, 0, PdfEncryption.metadataPad.Length);
				encryptionKey = new byte[sh.GetDigestSize()];
				sh.DoFinal(encryptionKey, 0);
			}
			decrypt = new PdfEncryption();
			decrypt.SetCryptoMode(cryptoMode, lengthValue);

			if (filter.Equals(PdfName.STANDARD)) {
				if (rValue == 5) {
					ownerPasswordUsed = decrypt.ReadKey(enc, password);
					decrypt.documentID = documentID;
					pValue = decrypt.GetPermissions();
				} else {
					//check by owner password
					decrypt.SetupByOwnerPassword(documentID, password, uValue, oValue, pValue);
					if (!EqualsArray(uValue, decrypt.userKey, (rValue == 3 || rValue == 4) ? 16 : 32)) {
						//check by user password
						decrypt.SetupByUserPassword(documentID, password, oValue, pValue);
						if (!EqualsArray(uValue, decrypt.userKey, (rValue == 3 || rValue == 4) ? 16 : 32)) {
							throw new BadPasswordException(MessageLocalization.GetComposedMessage("bad.user.password"));
						}
					} else
						ownerPasswordUsed = true;
				}
			} else if (filter.Equals(PdfName.PUBSEC)) {
				decrypt.documentID = documentID;
				if ((cryptoMode & PdfWriter.ENCRYPTION_MASK) == PdfWriter.ENCRYPTION_AES_256)
					decrypt.SetKey(encryptionKey);
				else
					decrypt.SetupByEncryptionKey(encryptionKey, lengthValue);
				ownerPasswordUsed = true;
			}
			for (var k = 0; k < strings.Count; ++k) {
				var str = strings[k];
				str.Decrypt(this);
			}
			if (encDic.IsIndirect()) {
				cryptoRef = (PRIndirectReference)encDic;
				xrefObj[cryptoRef.Number] = null;
			}
			encryptionError = false;
		}

		/**
		* @param obj
		* @return a PdfObject
		*/
		public static PdfObject GetPdfObjectRelease(PdfObject obj) {
			var obj2 = GetPdfObject(obj);
			ReleaseLastXrefPartial(obj);
			return obj2;
		}


		/**
		* Reads a <CODE>PdfObject</CODE> resolving an indirect reference
		* if needed.
		* @param obj the <CODE>PdfObject</CODE> to read
		* @return the resolved <CODE>PdfObject</CODE>
		*/
		public static PdfObject GetPdfObject(PdfObject obj) {
			if (obj == null)
				return null;
			if (!obj.IsIndirect())
				return obj;
			var refi = (PRIndirectReference)obj;
			var idx = refi.Number;
			var appendable = refi.Reader.appendable;
			obj = refi.Reader.GetPdfObject(idx);
			if (obj == null) {
				return null;
			} else {
				if (appendable) {
					switch (obj.Type) {
						case PdfObject.NULL:
							obj = new PdfNull();
							break;
						case PdfObject.BOOLEAN:
							obj = new PdfBoolean(((PdfBoolean)obj).BooleanValue);
							break;
						case PdfObject.NAME:
							obj = new PdfName(obj.GetBytes());
							break;
					}
					obj.IndRef = refi;
				}
				return obj;
			}
		}

		/**
		* Reads a <CODE>PdfObject</CODE> resolving an indirect reference
		* if needed. If the reader was opened in partial mode the object will be released
		* to save memory.
		* @param obj the <CODE>PdfObject</CODE> to read
		* @param parent
		* @return a PdfObject
		*/
		public static PdfObject GetPdfObjectRelease(PdfObject obj, PdfObject parent) {
			var obj2 = GetPdfObject(obj, parent);
			ReleaseLastXrefPartial(obj);
			return obj2;
		}

		/**
		* @param obj
		* @param parent
		* @return a PdfObject
		*/
		public static PdfObject GetPdfObject(PdfObject obj, PdfObject parent) {
			if (obj == null)
				return null;
			if (!obj.IsIndirect()) {
				PRIndirectReference refi = null;
				if (parent != null && (refi = parent.IndRef) != null && refi.Reader.Appendable) {
					switch (obj.Type) {
						case PdfObject.NULL:
							obj = new PdfNull();
							break;
						case PdfObject.BOOLEAN:
							obj = new PdfBoolean(((PdfBoolean)obj).BooleanValue);
							break;
						case PdfObject.NAME:
							obj = new PdfName(obj.GetBytes());
							break;
					}
					obj.IndRef = refi;
				}
				return obj;
			}
			return GetPdfObject(obj);
		}

		/**
		* @param idx
		* @return a PdfObject
		*/
		virtual public PdfObject GetPdfObjectRelease(int idx) {
			var obj = GetPdfObject(idx);
			ReleaseLastXrefPartial();
			return obj;
		}

		/**
		* @param idx
		* @return aPdfObject
		*/
		virtual public PdfObject GetPdfObject(int idx) {
			lastXrefPartial = -1;
			if (idx < 0 || idx >= xrefObj.Count)
				return null;
			var obj = xrefObj[idx];
			if (!partial || obj != null)
				return obj;
			if (idx * 2 >= xref.Length)
				return null;
			obj = ReadSingleObject(idx);
			lastXrefPartial = -1;
			if (obj != null)
				lastXrefPartial = idx;
			return obj;
		}

		/**
		* 
		*/
		virtual public void ResetLastXrefPartial() {
			lastXrefPartial = -1;
		}

		/**
		* 
		*/
		virtual public void ReleaseLastXrefPartial() {
			if (partial && lastXrefPartial != -1) {
				xrefObj[lastXrefPartial] = null;
				lastXrefPartial = -1;
			}
		}

		/**
		* @param obj
		*/
		public static void ReleaseLastXrefPartial(PdfObject obj) {
			if (obj == null)
				return;
			if (!obj.IsIndirect())
				return;
			if (!(obj is PRIndirectReference))
				return;
			var refi = (PRIndirectReference)obj;
			var reader = refi.Reader;
			if (reader.partial && reader.lastXrefPartial != -1 && reader.lastXrefPartial == refi.Number) {
				reader.xrefObj[reader.lastXrefPartial] = null;
			}
			reader.lastXrefPartial = -1;
		}

		private void SetXrefPartialObject(int idx, PdfObject obj) {
			if (!partial || idx < 0)
				return;
			xrefObj[idx] = obj;
		}

		/**
		* @param obj
		* @return an indirect reference
		*/
		virtual public PRIndirectReference AddPdfObject(PdfObject obj) {
			xrefObj.Add(obj);
			return new PRIndirectReference(this, xrefObj.Count - 1);
		}

		virtual protected internal void ReadPages() {
			catalog = trailer.GetAsDict(PdfName.ROOT);
			if (catalog == null)
				throw new InvalidPdfException(MessageLocalization.GetComposedMessage("the.document.has.no.catalog.object"));
			rootPages = catalog.GetAsDict(PdfName.PAGES);
			if (rootPages == null || (!PdfName.PAGES.Equals(rootPages.Get(PdfName.TYPE)) && (!PdfName.PAGES.Equals(rootPages.Get(new PdfName("Types")))))) {
				if (debugmode) {
					if (LOGGER.IsLogging(Level.ERROR)) {
						LOGGER.Error(MessageLocalization.GetComposedMessage("the.document.has.no.page.root"));
					}
				} else {
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("the.document.has.no.page.root"));
				}
			}

			pageRefs = new PageRefs(this);
		}

		virtual protected internal void ReadDocObjPartial() {
			xrefObj = new List<PdfObject>(xref.Length / 2);
			for (var k = 0; k < xref.Length / 2; ++k) {
				xrefObj.Add(null);
			}
			ReadDecryptedDocObj();
			if (objStmToOffset != null) {
				var keys = objStmToOffset.GetKeys();
				for (var k = 0; k < keys.Length; ++k) {
					var n = keys[k];
					objStmToOffset[n] = xref[n * 2];
					xref[n * 2] = -1;
				}
			}
		}

		virtual protected internal PdfObject ReadSingleObject(int k) {
			strings.Clear();
			var k2 = k * 2;
			var pos = xref[k2];
			if (pos < 0)
				return null;
			if (xref[k2 + 1] > 0)
				pos = objStmToOffset[xref[k2 + 1]];
			if (pos == 0)
				return null;
			tokens.Seek(pos);
			tokens.NextValidToken();
			if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
				tokens.ThrowError(MessageLocalization.GetComposedMessage("invalid.object.number"));
			objNum = tokens.IntValue;
			tokens.NextValidToken();
			if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
				tokens.ThrowError(MessageLocalization.GetComposedMessage("invalid.generation.number"));
			objGen = tokens.IntValue;
			tokens.NextValidToken();
			if (!tokens.StringValue.Equals("obj"))
				tokens.ThrowError(MessageLocalization.GetComposedMessage("token.obj.expected"));
			PdfObject obj;
			try {
				obj = ReadPRObject();
				for (var j = 0; j < strings.Count; ++j) {
					var str = strings[j];
					str.Decrypt(this);
				}
				if (obj.IsStream()) {
					CheckPRStreamLength((PRStream)obj);
				}
			} catch (IOException e) {
				if (debugmode) {
					if (LOGGER.IsLogging(Level.ERROR))
						LOGGER.Error(e.Message, e);
					obj = null;
				} else
					throw e;
			}
			if (xref[k2 + 1] > 0) {
				obj = ReadOneObjStm((PRStream)obj, (int)xref[k2]);
			}
			xrefObj[k] = obj;
			return obj;
		}

		virtual protected internal PdfObject ReadOneObjStm(PRStream stream, int idx) {
			var first = stream.GetAsNumber(PdfName.FIRST).IntValue;
			var b = GetStreamBytes(stream, tokens.File);
			var saveTokens = tokens;
			tokens = new PRTokeniser(new RandomAccessFileOrArray(new RandomAccessSourceFactory().CreateSource(b)));
			try {
				var address = 0;
				var ok = true;
				++idx;
				for (var k = 0; k < idx; ++k) {
					ok = tokens.NextToken();
					if (!ok)
						break;
					if (tokens.TokenType != PRTokeniser.TokType.NUMBER) {
						ok = false;
						break;
					}
					ok = tokens.NextToken();
					if (!ok)
						break;
					if (tokens.TokenType != PRTokeniser.TokType.NUMBER) {
						ok = false;
						break;
					}
					address = tokens.IntValue + first;
				}
				if (!ok)
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("error.reading.objstm"));
				tokens.Seek(address);
				tokens.NextToken();
				PdfObject obj;
				if (tokens.TokenType == PRTokeniser.TokType.NUMBER) {
					obj = new PdfNumber(tokens.StringValue);
				} else {
					tokens.Seek(address);
					obj = ReadPRObject();
				}
				return obj;
			} finally {
				tokens = saveTokens;
			}
		}

		/**
		* @return the percentage of the cross reference table that has been read
		*/
		virtual public double DumpPerc() {
			var total = 0;
			for (var k = 0; k < xrefObj.Count; ++k) {
				if (xrefObj[k] != null)
					++total;
			}
			return (total * 100.0 / xrefObj.Count);
		}

		virtual protected internal void ReadDocObj() {
			var streams = new List<PRStream>();
			xrefObj = new List<PdfObject>(xref.Length / 2);
			for (var k = 0; k < xref.Length / 2; ++k) {
				xrefObj.Add(null);
			}
			for (var k = 2; k < xref.Length; k += 2) {
				var pos = xref[k];
				if (pos <= 0 || xref[k + 1] > 0)
					continue;
				tokens.Seek(pos);
				tokens.NextValidToken();
				if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("invalid.object.number"));
				objNum = tokens.IntValue;
				tokens.NextValidToken();
				if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("invalid.generation.number"));
				objGen = tokens.IntValue;
				tokens.NextValidToken();
				if (!tokens.StringValue.Equals("obj"))
					tokens.ThrowError(MessageLocalization.GetComposedMessage("token.obj.expected"));
				PdfObject obj;
				try {
					obj = ReadPRObject();
					if (obj.IsStream()) {
						streams.Add((PRStream)obj);
					}
				} catch (IOException e) {
					if (debugmode) {
						if (LOGGER.IsLogging(Level.ERROR))
							LOGGER.Error(e.Message, e);
						obj = null;
					} else
						throw e;
				}
				xrefObj[k / 2] = obj;
			}
			for (var k = 0; k < streams.Count; ++k) {
				CheckPRStreamLength(streams[k]);
			}
			ReadDecryptedDocObj();
			if (objStmMark != null) {
				foreach (var entry in objStmMark) {
					var n = entry.Key;
					var h = entry.Value;
					ReadObjStm((PRStream)xrefObj[n], h);
					xrefObj[n] = null;
				}
				objStmMark = null;
			}
			xref = null;
		}

		private void CheckPRStreamLength(PRStream stream) {
			var fileLength = tokens.Length;
			var start = stream.Offset;
			var calc = false;
			long streamLength = 0;
			var obj = GetPdfObjectRelease(stream.Get(PdfName.LENGTH));
			if (obj != null && obj.Type == PdfObject.NUMBER) {
				streamLength = ((PdfNumber)obj).IntValue;
				if (streamLength + start > fileLength - 20)
					calc = true;
				else {
					tokens.Seek(start + streamLength);
					var line = tokens.ReadString(20);
					if (!line.StartsWith("\nendstream") &&
					!line.StartsWith("\r\nendstream") &&
					!line.StartsWith("\rendstream") &&
					!line.StartsWith("endstream"))
						calc = true;
				}
			} else
				calc = true;
			if (calc) {
				var tline = new byte[16];
				tokens.Seek(start);
				long pos;
				while (true) {
					pos = tokens.FilePointer;
					if (!tokens.ReadLineSegment(tline, false)) // added boolean because of mailing list issue (17 Feb. 2014)
						break;
					if (Equalsn(tline, endstream)) {
						streamLength = pos - start;
						break;
					}
					if (Equalsn(tline, endobj)) {
						tokens.Seek(pos - 16);
						var s = tokens.ReadString(16);
						var index = s.IndexOf("endstream");
						if (index >= 0)
							pos = pos - 16 + index;
						streamLength = pos - start;
						break;
					}
				}
				tokens.Seek(pos - 2);
				if (tokens.Read() == 13)
					streamLength--;
				tokens.Seek(pos - 1);
				if (tokens.Read() == 10)
					streamLength--;

				if (streamLength < 0) {
					streamLength = 0;
				}
			}
			stream.Length = (int)streamLength;
		}

		virtual protected internal void ReadObjStm(PRStream stream, IntHashtable map) {
			if (stream == null) return;
			var first = stream.GetAsNumber(PdfName.FIRST).IntValue;
			var n = stream.GetAsNumber(PdfName.N).IntValue;
			var b = GetStreamBytes(stream, tokens.File);
			var saveTokens = tokens;
			tokens = new PRTokeniser(new RandomAccessFileOrArray(new RandomAccessSourceFactory().CreateSource(b)));
			try {
				var address = new int[n];
				var objNumber = new int[n];
				var ok = true;
				for (var k = 0; k < n; ++k) {
					ok = tokens.NextToken();
					if (!ok)
						break;
					if (tokens.TokenType != PRTokeniser.TokType.NUMBER) {
						ok = false;
						break;
					}
					objNumber[k] = tokens.IntValue;
					ok = tokens.NextToken();
					if (!ok)
						break;
					if (tokens.TokenType != PRTokeniser.TokType.NUMBER) {
						ok = false;
						break;
					}
					address[k] = tokens.IntValue + first;
				}
				if (!ok)
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("error.reading.objstm"));
				for (var k = 0; k < n; ++k) {
					if (map.ContainsKey(k)) {
						tokens.Seek(address[k]);
						tokens.NextToken();
						PdfObject obj;
						if (tokens.TokenType == PRTokeniser.TokType.NUMBER) {
							obj = new PdfNumber(tokens.StringValue);
						} else {
							tokens.Seek(address[k]);
							obj = ReadPRObject();
						}
						xrefObj[objNumber[k]] = obj;
					}
				}
			} finally {
				tokens = saveTokens;
			}
		}

		/**
		* Eliminates the reference to the object freeing the memory used by it and clearing
		* the xref entry.
		* @param obj the object. If it's an indirect reference it will be eliminated
		* @return the object or the already erased dereferenced object
		*/
		public static PdfObject KillIndirect(PdfObject obj) {
			if (obj == null || obj.IsNull())
				return null;
			var ret = GetPdfObjectRelease(obj);
			if (obj.IsIndirect()) {
				var refi = (PRIndirectReference)obj;
				var reader = refi.Reader;
				var n = refi.Number;
				reader.xrefObj[n] = null;
				if (reader.partial)
					reader.xref[n * 2] = -1;
			}
			return ret;
		}

		private void EnsureXrefSize(int size) {
			if (size == 0)
				return;
			if (xref == null)
				xref = new long[size];
			else {
				if (xref.Length < size) {
					var xref2 = new long[size];
					Array.Copy(xref, 0, xref2, 0, xref.Length);
					xref = xref2;
				}
			}
		}

		virtual protected internal void ReadXref() {
			hybridXref = false;
			newXrefType = false;
			tokens.Seek(tokens.GetStartxref());
			tokens.NextToken();
			if (!tokens.StringValue.Equals("startxref"))
				throw new InvalidPdfException(MessageLocalization.GetComposedMessage("startxref.not.found"));
			tokens.NextToken();
			if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
				throw new InvalidPdfException(MessageLocalization.GetComposedMessage("startxref.is.not.followed.by.a.number"));
			var startxref = tokens.LongValue;
			lastXref = startxref;
			eofPos = tokens.FilePointer;
			try {
				if (ReadXRefStream(startxref)) {
					newXrefType = true;
					return;
				}
			} catch { }
			xref = null;
			tokens.Seek(startxref);
			trailer = ReadXrefSection();
			var trailer2 = trailer;
			while (true) {
				var prev = (PdfNumber)trailer2.Get(PdfName.PREV);
				if (prev == null)
					break;
				if (prev.LongValue == startxref) {
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("trailer.prev.entry.points.to.its.own.cross.reference.section"));
				}
				startxref = prev.LongValue;
				tokens.Seek(startxref);
				trailer2 = ReadXrefSection();
			}
		}

		virtual protected internal PdfDictionary ReadXrefSection() {
			tokens.NextValidToken();
			if (!tokens.StringValue.Equals("xref"))
				tokens.ThrowError(MessageLocalization.GetComposedMessage("xref.subsection.not.found"));
			var start = 0;
			var end = 0;
			long pos = 0;
			var gen = 0;
			while (true) {
				tokens.NextValidToken();
				if (tokens.StringValue.Equals("trailer"))
					break;
				if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("object.number.of.the.first.object.in.this.xref.subsection.not.found"));
				start = tokens.IntValue;
				tokens.NextValidToken();
				if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("number.of.entries.in.this.xref.subsection.not.found"));
				end = tokens.IntValue + start;
				if (start == 1) { // fix incorrect start number
					var back = tokens.FilePointer;
					tokens.NextValidToken();
					pos = tokens.LongValue;
					tokens.NextValidToken();
					gen = tokens.IntValue;
					if (pos == 0 && gen == PdfWriter.GENERATION_MAX) {
						--start;
						--end;
					}
					tokens.Seek(back);
				}
				EnsureXrefSize(end * 2);
				for (var k = start; k < end; ++k) {
					tokens.NextValidToken();
					pos = tokens.LongValue;
					tokens.NextValidToken();
					gen = tokens.IntValue;
					tokens.NextValidToken();
					var p = k * 2;
					if (tokens.StringValue.Equals("n")) {
						if (xref[p] == 0 && xref[p + 1] == 0) {
							//                        if (pos == 0)
							//                            tokens.ThrowError(MessageLocalization.GetComposedMessage("file.position.0.cross.reference.entry.in.this.xref.subsection"));
							xref[p] = pos;
						}
					} else if (tokens.StringValue.Equals("f")) {
						if (xref[p] == 0 && xref[p + 1] == 0)
							xref[p] = -1;
					} else
						tokens.ThrowError(MessageLocalization.GetComposedMessage("invalid.cross.reference.entry.in.this.xref.subsection"));
				}
			}
			var trailer = (PdfDictionary)ReadPRObject();
			var xrefSize = (PdfNumber)trailer.Get(PdfName.SIZE);
			EnsureXrefSize(xrefSize.IntValue * 2);
			var xrs = trailer.Get(PdfName.XREFSTM);
			if (xrs != null && xrs.IsNumber()) {
				var loc = ((PdfNumber)xrs).IntValue;
				try {
					ReadXRefStream(loc);
					newXrefType = true;
					hybridXref = true;
				} catch (IOException e) {
					xref = null;
					throw e;
				}
			}
			return trailer;
		}

		virtual protected internal bool ReadXRefStream(long ptr) {
			tokens.Seek(ptr);
			var thisStream = 0;
			if (!tokens.NextToken())
				return false;
			if (tokens.TokenType != PRTokeniser.TokType.NUMBER)
				return false;
			thisStream = tokens.IntValue;
			if (!tokens.NextToken() || tokens.TokenType != PRTokeniser.TokType.NUMBER)
				return false;
			if (!tokens.NextToken() || !tokens.StringValue.Equals("obj"))
				return false;
			var objecto = ReadPRObject();
			PRStream stm = null;
			if (objecto.IsStream()) {
				stm = (PRStream)objecto;
				if (!PdfName.XREF.Equals(stm.Get(PdfName.TYPE)))
					return false;
			} else
				return false;
			if (trailer == null) {
				trailer = new PdfDictionary();
				trailer.Merge(stm);
			}
			stm.Length = ((PdfNumber)stm.Get(PdfName.LENGTH)).IntValue;
			var size = ((PdfNumber)stm.Get(PdfName.SIZE)).IntValue;
			PdfArray index;
			var obj = stm.Get(PdfName.INDEX);
			if (obj == null) {
				index = new PdfArray();
				index.Add(new int[] { 0, size });
			} else
				index = (PdfArray)obj;
			var w = (PdfArray)stm.Get(PdfName.W);
			long prev = -1;
			obj = stm.Get(PdfName.PREV);
			if (obj != null)
				prev = ((PdfNumber)obj).LongValue;
			// Each xref pair is a position
			// type 0 -> -1, 0
			// type 1 -> offset, 0
			// type 2 -> index, obj num
			EnsureXrefSize(size * 2);
			if (objStmMark == null && !partial)
				objStmMark = new Dictionary<int, IntHashtable>();
			if (objStmToOffset == null && partial)
				objStmToOffset = new LongHashtable();
			var b = GetStreamBytes(stm, tokens.File);
			var bptr = 0;
			var wc = new int[3];
			for (var k = 0; k < 3; ++k)
				wc[k] = w.GetAsNumber(k).IntValue;
			for (var idx = 0; idx < index.Size; idx += 2) {
				var start = index.GetAsNumber(idx).IntValue;
				var length = index.GetAsNumber(idx + 1).IntValue;
				EnsureXrefSize((start + length) * 2);
				while (length-- > 0) {
					var type = 1;
					if (wc[0] > 0) {
						type = 0;
						for (var k = 0; k < wc[0]; ++k)
							type = (type << 8) + (b[bptr++] & 0xff);
					}
					long field2 = 0;
					for (var k = 0; k < wc[1]; ++k)
						field2 = (field2 << 8) + (b[bptr++] & 0xff);
					var field3 = 0;
					for (var k = 0; k < wc[2]; ++k)
						field3 = (field3 << 8) + (b[bptr++] & 0xff);
					var baseb = start * 2;
					if (xref[baseb] == 0 && xref[baseb + 1] == 0) {
						switch (type) {
							case 0:
								xref[baseb] = -1;
								break;
							case 1:
								xref[baseb] = field2;
								break;
							case 2:
								xref[baseb] = field3;
								xref[baseb + 1] = field2;
								if (partial) {
									objStmToOffset[field2] = 0;
								} else {
									IntHashtable seq;
									if (!objStmMark.TryGetValue((int)field2, out seq)) {
										seq = new IntHashtable();
										seq[field3] = 1;
										objStmMark[(int)field2] = seq;
									} else
										seq[field3] = 1;
								}
								break;
						}
					}
					++start;
				}
			}
			thisStream *= 2;
			if (thisStream + 1 < xref.Length && xref[thisStream] == 0 && xref[thisStream + 1] == 0)
				xref[thisStream] = -1;

			if (prev == -1)
				return true;
			return ReadXRefStream(prev);
		}

		virtual protected internal void RebuildXref() {
			hybridXref = false;
			newXrefType = false;
			tokens.Seek(0);
			var xr = new long[1024][];
			long top = 0;
			trailer = null;
			var line = new byte[64];
			for (; ; ) {
				var pos = tokens.FilePointer;
				if (!tokens.ReadLineSegment(line, true)) // added boolean because of mailing list issue (17 Feb. 2014)
					break;
				if (line[0] == 't') {
					if (!PdfEncodings.ConvertToString(line, null).StartsWith("trailer"))
						continue;
					tokens.Seek(pos);
					tokens.NextToken();
					pos = tokens.FilePointer;
					try {
						var dic = (PdfDictionary)ReadPRObject();
						if (dic.Get(PdfName.ROOT) != null)
							trailer = dic;
						else
							tokens.Seek(pos);
					} catch {
						tokens.Seek(pos);
					}
				} else if (line[0] >= '0' && line[0] <= '9') {
					var obj = PRTokeniser.CheckObjectStart(line);
					if (obj == null)
						continue;
					var num = obj[0];
					var gen = obj[1];
					if (num >= xr.Length) {
						var newLength = num * 2;
						var xr2 = new long[newLength][];
						Array.Copy(xr, 0, xr2, 0, top);
						xr = xr2;
					}
					if (num >= top)
						top = num + 1;
					if (xr[num] == null || gen >= xr[num][1]) {
						obj[0] = pos;
						xr[num] = obj;
					}
				}
			}
			if (trailer == null)
				throw new InvalidPdfException(MessageLocalization.GetComposedMessage("trailer.not.found"));
			xref = new long[top * 2];
			for (var k = 0; k < top; ++k) {
				var obj = xr[k];
				if (obj != null)
					xref[k * 2] = obj[0];
			}
		}

		virtual protected internal PdfDictionary ReadDictionary() {
			var dic = new PdfDictionary();
			while (true) {
				tokens.NextValidToken();
				if (tokens.TokenType == PRTokeniser.TokType.END_DIC)
					break;
				if (tokens.TokenType != PRTokeniser.TokType.NAME)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("dictionary.key.1.is.not.a.name", tokens.StringValue));
				var name = new PdfName(tokens.StringValue, false);
				var obj = ReadPRObject();
				var type = obj.Type;
				if (-type == (int)PRTokeniser.TokType.END_DIC)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("unexpected.gt.gt"));
				if (-type == (int)PRTokeniser.TokType.END_ARRAY)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("unexpected.close.bracket"));
				dic.Put(name, obj);
			}
			return dic;
		}

		virtual protected internal PdfArray ReadArray() {
			var array = new PdfArray();
			while (true) {
				var obj = ReadPRObject();
				var type = obj.Type;
				if (-type == (int)PRTokeniser.TokType.END_ARRAY)
					break;
				if (-type == (int)PRTokeniser.TokType.END_DIC)
					tokens.ThrowError(MessageLocalization.GetComposedMessage("unexpected.gt.gt"));
				array.Add(obj);
			}
			return array;
		}

		// Track how deeply nested the current object is, so
		// we know when to return an individual null or boolean, or
		// reuse one of the static ones.
		private int readDepth = 0;

		virtual protected internal PdfObject ReadPRObject() {
			tokens.NextValidToken();
			var type = tokens.TokenType;
			switch (type) {
				case PRTokeniser.TokType.START_DIC: {
					++readDepth;
					var dic = ReadDictionary();
					--readDepth;
					var pos = tokens.FilePointer;
					// be careful in the trailer. May not be a "next" token.
					bool hasNext;
					do {
						hasNext = tokens.NextToken();
					} while (hasNext && tokens.TokenType == PRTokeniser.TokType.COMMENT);

					if (hasNext && tokens.StringValue.Equals("stream")) {
						//skip whitespaces
						int ch;
						do {
							ch = tokens.Read();
						} while (ch == 32 || ch == 9 || ch == 0 || ch == 12);
						if (ch != '\n')
							ch = tokens.Read();
						if (ch != '\n')
							tokens.BackOnePosition(ch);
						var stream = new PRStream(this, tokens.FilePointer);
						stream.Merge(dic);
						stream.ObjNum = objNum;
						stream.ObjGen = objGen;
						return stream;
					} else {
						tokens.Seek(pos);
						return dic;
					}
				}
				case PRTokeniser.TokType.START_ARRAY: {
					++readDepth;
					var arr = ReadArray();
					--readDepth;
					return arr;
				}
				case PRTokeniser.TokType.NUMBER:
					return new PdfNumber(tokens.StringValue);
				case PRTokeniser.TokType.STRING:
					var str = new PdfString(tokens.StringValue, null).SetHexWriting(tokens.IsHexString());
					str.SetObjNum(objNum, objGen);
					if (strings != null)
						strings.Add(str);
					return str;
				case PRTokeniser.TokType.NAME: {
					PdfName cachedName;
					PdfName.staticNames.TryGetValue(tokens.StringValue, out cachedName);
					if (readDepth > 0 && cachedName != null) {
						return cachedName;
					} else {
						// an indirect name (how odd...), or a non-standard one
						return new PdfName(tokens.StringValue, false);
					}
				}
				case PRTokeniser.TokType.REF: {
					var num = tokens.Reference;
					if (num >= 0) {
						return new PRIndirectReference(this, num, tokens.Generation);
					} else {
						if (LOGGER.IsLogging(Level.ERROR)) {
							LOGGER.Error(MessageLocalization.GetComposedMessage("invalid.reference.number.skip"));
						}
						return PdfNull.PDFNULL;
					}
				}
				case PRTokeniser.TokType.ENDOFFILE:
					throw new IOException(MessageLocalization.GetComposedMessage("unexpected.end.of.file"));
				default:
					var sv = tokens.StringValue;
					if ("null".Equals(sv)) {
						if (readDepth == 0) {
							return new PdfNull();
						} //else
						return PdfNull.PDFNULL;
					} else if ("true".Equals(sv)) {
						if (readDepth == 0) {
							return new PdfBoolean(true);
						} //else
						return PdfBoolean.PDFTRUE;
					} else if ("false".Equals(sv)) {
						if (readDepth == 0) {
							return new PdfBoolean(false);
						} //else
						return PdfBoolean.PDFFALSE;
					}
					return new PdfLiteral(-(int)type, tokens.StringValue);
			}
		}

		/** Decodes a stream that has the FlateDecode filter.
		* @param in the input data
		* @return the decoded data
		*/
		public static byte[] FlateDecode(byte[] inp) {
			var b = FlateDecode(inp, true);
			if (b == null)
				return FlateDecode(inp, false);
			return b;
		}

		/** Decodes a stream that has the FlateDecode filter.
		* @param in the input data
		* @return the decoded data
		*/
		internal static byte[] FlateDecode(byte[] inp, MemoryStream outS) {
			var b = FlateDecode(inp, true, outS);
			if (b == null) {
				return FlateDecode(inp, false, outS);
			}
			return b;
		}

		/**
		* @param in
		* @param dicPar
		* @return a byte array
		*/
		public static byte[] DecodePredictor(byte[] inp, PdfObject dicPar) {
			if (dicPar == null || !dicPar.IsDictionary())
				return inp;
			var dic = (PdfDictionary)dicPar;
			var obj = GetPdfObject(dic.Get(PdfName.PREDICTOR));
			if (obj == null || !obj.IsNumber())
				return inp;
			var predictor = ((PdfNumber)obj).IntValue;
			if (predictor < 10 && predictor != 2)
				return inp;
			var width = 1;
			obj = GetPdfObject(dic.Get(PdfName.COLUMNS));
			if (obj != null && obj.IsNumber())
				width = ((PdfNumber)obj).IntValue;
			var colors = 1;
			obj = GetPdfObject(dic.Get(PdfName.COLORS));
			if (obj != null && obj.IsNumber())
				colors = ((PdfNumber)obj).IntValue;
			var bpc = 8;
			obj = GetPdfObject(dic.Get(PdfName.BITSPERCOMPONENT));
			if (obj != null && obj.IsNumber())
				bpc = ((PdfNumber)obj).IntValue;
			var dataStream = new MemoryStream(inp);
			var fout = new MemoryStream(inp.Length);
			var bytesPerPixel = colors * bpc / 8;
			var bytesPerRow = (colors * width * bpc + 7) / 8;
			var curr = new byte[bytesPerRow];
			var prior = new byte[bytesPerRow];

			if (predictor == 2) {
				if (bpc == 8) {
					var numRows = inp.Length / bytesPerRow;
					for (var row = 0; row < numRows; row++) {
						var rowStart = row * bytesPerRow;
						for (var col = 0 + bytesPerPixel; col < bytesPerRow; col++) {
							inp[rowStart + col] = (byte)(inp[rowStart + col] + inp[rowStart + col - bytesPerPixel]);
						}
					}
				}
				return inp;
			}

			// Decode the (sub)image row-by-row
			while (true) {
				// Read the filter type byte and a row of data
				var filter = 0;
				try {
					filter = dataStream.ReadByte();
					if (filter < 0) {
						return fout.ToArray();
					}
					var tot = 0;
					while (tot < bytesPerRow) {
						var n = dataStream.Read(curr, tot, bytesPerRow - tot);
						if (n <= 0)
							return fout.ToArray();
						tot += n;
					}
				} catch {
					return fout.ToArray();
				}

				switch (filter) {
					case 0: //PNG_FILTER_NONE
						break;
					case 1: //PNG_FILTER_SUB
						for (var i = bytesPerPixel; i < bytesPerRow; i++) {
							curr[i] += curr[i - bytesPerPixel];
						}
						break;
					case 2: //PNG_FILTER_UP
						for (var i = 0; i < bytesPerRow; i++) {
							curr[i] += prior[i];
						}
						break;
					case 3: //PNG_FILTER_AVERAGE
						for (var i = 0; i < bytesPerPixel; i++) {
							curr[i] += (byte)(prior[i] / 2);
						}
						for (var i = bytesPerPixel; i < bytesPerRow; i++) {
							curr[i] += (byte)(((curr[i - bytesPerPixel] & 0xff) + (prior[i] & 0xff)) / 2);
						}
						break;
					case 4: //PNG_FILTER_PAETH
						for (var i = 0; i < bytesPerPixel; i++) {
							curr[i] += prior[i];
						}

						for (var i = bytesPerPixel; i < bytesPerRow; i++) {
							var a = curr[i - bytesPerPixel] & 0xff;
							var b = prior[i] & 0xff;
							var c = prior[i - bytesPerPixel] & 0xff;

							var p = a + b - c;
							var pa = Math.Abs(p - a);
							var pb = Math.Abs(p - b);
							var pc = Math.Abs(p - c);

							int ret;

							if ((pa <= pb) && (pa <= pc)) {
								ret = a;
							} else if (pb <= pc) {
								ret = b;
							} else {
								ret = c;
							}
							curr[i] += (byte)(ret);
						}
						break;
					default:
						// Error -- uknown filter type
						throw new Exception(MessageLocalization.GetComposedMessage("png.filter.unknown"));
				}
				fout.Write(curr, 0, curr.Length);

				// Swap curr and prior
				var tmp = prior;
				prior = curr;
				curr = tmp;
			}
		}

		/** A helper to FlateDecode.
		* @param in the input data
		* @param strict <CODE>true</CODE> to read a correct stream. <CODE>false</CODE>
		* to try to read a corrupted stream
		* @return the decoded data
		*/
		public static byte[] FlateDecode(byte[] inp, bool strict) {
			return FlateDecode(inp, strict, new MemoryStream());
		}

		/** A helper to FlateDecode.
		* @param in the input data
		* @param strict <CODE>true</CODE> to read a correct stream. <CODE>false</CODE>
		* to try to read a corrupted stream
		* @return the decoded data
		*/
		internal static byte[] FlateDecode(byte[] inp, bool strict, MemoryStream outp) {
			var stream = new MemoryStream(inp);
			var zip = new ZInflaterInputStream(stream);
			var b = new byte[strict ? 4092 : 1];
			try {
				int n;
				while ((n = zip.Read(b, 0, b.Length)) > 0) {
					outp.Write(b, 0, n);
				}
				zip.Close();
				outp.Close();
				return outp.ToArray();
			} catch (MemoryLimitsAwareException e) {
				throw e;
			} catch (Exception) {
				if (strict)
					return null;
				return outp.ToArray();
			} finally {
				try {
					zip.Close();
				} catch (IOException) {
				}
				try {
					outp.Close();
				} catch (IOException) {
				}

			}
		}

		/** Decodes a stream that has the ASCIIHexDecode filter.
		 * @param in the input data
		* @return the decoded data
		*/
		public static byte[] ASCIIHexDecode(byte[] inp) {
			return ASCIIHexDecode(inp, new MemoryStream());
		}

		/** Decodes a stream that has the ASCIIHexDecode filter.
		* @param in the input data
		* @return the decoded data
		*/
		internal static byte[] ASCIIHexDecode(byte[] inp, MemoryStream outp) {
			var first = true;
			var n1 = 0;
			for (var k = 0; k < inp.Length; ++k) {
				var ch = inp[k] & 0xff;
				if (ch == '>')
					break;
				if (PRTokeniser.IsWhitespace(ch))
					continue;
				var n = PRTokeniser.GetHex(ch);
				if (n == -1)
					throw new ArgumentException(MessageLocalization.GetComposedMessage("illegal.character.in.asciihexdecode"));
				if (first)
					n1 = n;
				else
					outp.WriteByte((byte)((n1 << 4) + n));
				first = !first;
			}
			if (!first)
				outp.WriteByte((byte)(n1 << 4));
			return outp.ToArray();
		}

		/** Decodes a stream that has the ASCII85Decode filter.
		* @param in the input data
		* @return the decoded data
		*/
		public static byte[] ASCII85Decode(byte[] inp) {
			return ASCII85Decode(inp, new MemoryStream());
		}

		/** Decodes a stream that has the ASCII85Decode filter.
		* @param in the input data
		* @return the decoded data
		*/
		internal static byte[] ASCII85Decode(byte[] inp, MemoryStream outp) {
			var state = 0;
			var chn = new int[5];
			for (var k = 0; k < inp.Length; ++k) {
				var ch = inp[k] & 0xff;
				if (ch == '~')
					break;
				if (PRTokeniser.IsWhitespace(ch))
					continue;
				if (ch == 'z' && state == 0) {
					outp.WriteByte(0);
					outp.WriteByte(0);
					outp.WriteByte(0);
					outp.WriteByte(0);
					continue;
				}
				if (ch < '!' || ch > 'u')
					throw new ArgumentException(MessageLocalization.GetComposedMessage("illegal.character.in.ascii85decode"));
				chn[state] = ch - '!';
				++state;
				if (state == 5) {
					state = 0;
					var rx = 0;
					for (var j = 0; j < 5; ++j)
						rx = rx * 85 + chn[j];
					outp.WriteByte((byte)(rx >> 24));
					outp.WriteByte((byte)(rx >> 16));
					outp.WriteByte((byte)(rx >> 8));
					outp.WriteByte((byte)rx);
				}
			}
			var r = 0;
			// We'll ignore the next two lines for the sake of perpetuating broken PDFs
			//            if (state == 1)
			//                throw new ArgumentException(MessageLocalization.GetComposedMessage("illegal.length.in.ascii85decode"));
			if (state == 2) {
				r = chn[0] * 85 * 85 * 85 * 85 + chn[1] * 85 * 85 * 85 + 85 * 85 * 85 + 85 * 85 + 85;
				outp.WriteByte((byte)(r >> 24));
			} else if (state == 3) {
				r = chn[0] * 85 * 85 * 85 * 85 + chn[1] * 85 * 85 * 85 + chn[2] * 85 * 85 + 85 * 85 + 85;
				outp.WriteByte((byte)(r >> 24));
				outp.WriteByte((byte)(r >> 16));
			} else if (state == 4) {
				r = chn[0] * 85 * 85 * 85 * 85 + chn[1] * 85 * 85 * 85 + chn[2] * 85 * 85 + chn[3] * 85 + 85;
				outp.WriteByte((byte)(r >> 24));
				outp.WriteByte((byte)(r >> 16));
				outp.WriteByte((byte)(r >> 8));
			}
			return outp.ToArray();
		}

		/** Decodes a stream that has the LZWDecode filter.
		* @param in the input data
		 * @return the decoded data
		*/
		public static byte[] LZWDecode(byte[] inp) {
			return LZWDecode(inp, new MemoryStream());
		}

		/** Decodes a stream that has the LZWDecode filter.
		* @param in the input data
		* @return the decoded data
		*/
		internal static byte[] LZWDecode(byte[] inp, MemoryStream outp) {
			var lzw = new LZWDecoder();
			lzw.Decode(inp, outp);
			return outp.ToArray();
		}

		/** Checks if the document had errors and was rebuilt.
		* @return true if rebuilt.
		*
		*/
		virtual public bool IsRebuilt() {
			return this.rebuilt;
		}

		/** Gets the dictionary that represents a page.
		* @param pageNum the page number. 1 is the first
		* @return the page dictionary
		*/
		virtual public PdfDictionary GetPageN(int pageNum) {
			var dic = pageRefs.GetPageN(pageNum);
			if (dic == null)
				return null;
			if (appendable)
				dic.IndRef = pageRefs.GetPageOrigRef(pageNum);
			return dic;
		}

		/**
		* @param pageNum
		* @return a Dictionary object
		*/
		virtual public PdfDictionary GetPageNRelease(int pageNum) {
			var dic = GetPageN(pageNum);
			pageRefs.ReleasePage(pageNum);
			return dic;
		}

		/**
		* @param pageNum
		*/
		virtual public void ReleasePage(int pageNum) {
			pageRefs.ReleasePage(pageNum);
		}

		/**
		* 
		*/
		virtual public void ResetReleasePage() {
			pageRefs.ResetReleasePage();
		}

		/** Gets the page reference to this page.
		* @param pageNum the page number. 1 is the first
		* @return the page reference
		*/
		virtual public PRIndirectReference GetPageOrigRef(int pageNum) {
			return pageRefs.GetPageOrigRef(pageNum);
		}

		/** Gets the contents of the page.
		* @param pageNum the page number. 1 is the first
		* @param file the location of the PDF document
		* @throws IOException on error
		* @return the content
		*/
		virtual public byte[] GetPageContent(int pageNum, RandomAccessFileOrArray file) {
			var page = GetPageNRelease(pageNum);
			if (page == null)
				return null;
			var contents = GetPdfObjectRelease(page.Get(PdfName.CONTENTS));
			if (contents == null)
				return new byte[0];
			var handler = memoryLimitsAwareHandler;
			var usedMemory = null == handler ? -1 : handler.GetAllMemoryUsedForDecompression();
			if (contents.IsStream()) {
				return GetStreamBytes((PRStream)contents, file);
			} else if (contents.IsArray()) {
				var array = (PdfArray)contents;
				var bout = new MemoryLimitsAwareOutputStream();
				for (var k = 0; k < array.Size; ++k) {
					var item = GetPdfObjectRelease(array.GetPdfObject(k));
					if (item == null || !item.IsStream())
						continue;
					var b = GetStreamBytes((PRStream)item, file);
					// usedMemory has changed, that means that some of currently processed pdf streams are suspicious
					if (null != handler && usedMemory < handler.GetAllMemoryUsedForDecompression()) {
						bout.SetMaxStreamSize(handler.GetMaxSizeOfSingleDecompressedPdfStream());
					}
					bout.Write(b, 0, b.Length);
					if (k != array.Size - 1)
						bout.WriteByte((byte)'\n');
				}
				return bout.ToArray();
			} else
				return new byte[0];
		}

		/** Gets the content from the page dictionary.
		 * @param page the page dictionary
		 * @throws IOException on error
		 * @return the content
		 * @since 5.0.6
		 */
		public static byte[] GetPageContent(PdfDictionary page) {
			if (page == null)
				return null;
			RandomAccessFileOrArray rf = null;
			try {
				var contents = GetPdfObjectRelease(page.Get(PdfName.CONTENTS));
				if (contents == null)
					return new byte[0];
				if (contents.IsStream()) {
					if (rf == null) {
						rf = ((PRStream)contents).Reader.SafeFile;
						rf.ReOpen();
					}
					return GetStreamBytes((PRStream)contents, rf);
				} else if (contents.IsArray()) {
					var array = (PdfArray)contents;
					var bout = new MemoryStream();
					for (var k = 0; k < array.Size; ++k) {
						var item = GetPdfObjectRelease(array.GetPdfObject(k));
						if (item == null || !item.IsStream())
							continue;
						if (rf == null) {
							rf = ((PRStream)item).Reader.SafeFile;
							rf.ReOpen();
						}
						var b = GetStreamBytes((PRStream)item, rf);
						bout.Write(b, 0, b.Length);
						if (k != array.Size - 1)
							bout.WriteByte((byte)'\n');
					}
					return bout.ToArray();
				} else
					return new byte[0];
			} finally {
				try {
					if (rf != null)
						rf.Close();
				} catch { }
			}
		}

		/**
		 * Retrieve the given page's resource dictionary
		 * @param pageNum 1-based page number from which to retrieve the resource dictionary
		 * @return The page's resources, or 'null' if the page has none.
		 * @since 5.1
		 */
		virtual public PdfDictionary GetPageResources(int pageNum) {
			return GetPageResources(GetPageN(pageNum));
		}

		/**
		 * Retrieve the given page's resource dictionary
		 * @param pageDict the given page
		 * @return The page's resources, or 'null' if the page has none.
		 * @since 5.1
		 */
		virtual public PdfDictionary GetPageResources(PdfDictionary pageDict) {
			return pageDict.GetAsDict(PdfName.RESOURCES);
		}

		/** Gets the contents of the page.
		* @param pageNum the page number. 1 is the first
		* @throws IOException on error
		* @return the content
		*/
		virtual public byte[] GetPageContent(int pageNum) {
			var rf = SafeFile;
			try {
				rf.ReOpen();
				return GetPageContent(pageNum, rf);
			} finally {
				try { rf.Close(); } catch { }
			}
		}

		virtual protected internal void KillXref(PdfObject obj) {
			if (obj == null)
				return;
			if ((obj is PdfIndirectReference) && !obj.IsIndirect())
				return;
			switch (obj.Type) {
				case PdfObject.INDIRECT: {
					var xr = ((PRIndirectReference)obj).Number;
					obj = xrefObj[xr];
					xrefObj[xr] = null;
					freeXref = xr;
					KillXref(obj);
					break;
				}
				case PdfObject.ARRAY: {
					var t = (PdfArray)obj;
					for (var i = 0; i < t.Size; ++i)
						KillXref(t.GetPdfObject(i));
					break;
				}
				case PdfObject.STREAM:
				case PdfObject.DICTIONARY: {
					var dic = (PdfDictionary)obj;
					foreach (var key in dic.Keys) {
						KillXref(dic.Get(key));
					}
					break;
				}
			}
		}

		/** Sets the contents of the page.
		* @param content the new page content
		* @param pageNum the page number. 1 is the first
		* @throws IOException on error
		*/
		virtual public void SetPageContent(int pageNum, byte[] content) {
			SetPageContent(pageNum, content, PdfStream.DEFAULT_COMPRESSION, false);
		}

		/** Sets the contents of the page.
		* @param content the new page content
		* @param pageNum the page number. 1 is the first
		* @since   2.1.3   (the method already existed without param compressionLevel)
		*/
		virtual public void SetPageContent(int pageNum, byte[] content, int compressionLevel, bool killOldXRefRecursively) {
			var page = GetPageN(pageNum);
			if (page == null)
				return;
			var contents = page.Get(PdfName.CONTENTS);
			freeXref = -1;
			if (killOldXRefRecursively) {
				KillXref(contents);
			}
			if (freeXref == -1) {
				xrefObj.Add(null);
				freeXref = xrefObj.Count - 1;
			}
			page.Put(PdfName.CONTENTS, new PRIndirectReference(this, freeXref));
			xrefObj[freeXref] = new PRStream(this, content, compressionLevel);
		}

		/**
		 * Decode a byte[] applying the filters specified in the provided dictionary using default filter handlers.
		 * @param b the bytes to decode
		 * @param streamDictionary the dictionary that contains filter information
		 * @return the decoded bytes
		 * @throws IOException if there are any problems decoding the bytes
		 * @since 5.0.4
		 */
		public static byte[] DecodeBytes(byte[] b, PdfDictionary streamDictionary) {
			return DecodeBytes(b, streamDictionary, FilterHandlers.GetDefaultFilterHandlers());
		}

		/**
		 * Decode a byte[] applying the filters specified in the provided dictionary using the provided filter handlers.
		 * @param b the bytes to decode
		 * @param streamDictionary the dictionary that contains filter information
		 * @param filterHandlers the map used to look up a handler for each type of filter
		 * @return the decoded bytes
		 * @throws IOException if there are any problems decoding the bytes
		 * @since 5.0.4
		 */
		public static byte[] DecodeBytes(byte[] b, PdfDictionary streamDictionary, IDictionary<PdfName, FilterHandlers.IFilterHandler> filterHandlers) {
			var filter = GetPdfObjectRelease(streamDictionary.Get(PdfName.FILTER));
			var filters = new List<PdfObject>();
			if (filter != null) {
				if (filter.IsName())
					filters.Add(filter);
				else if (filter.IsArray())
					filters = ((PdfArray)filter).ArrayList;
			}
			MemoryLimitsAwareHandler memoryLimitsAwareHandler = null;
			if (streamDictionary is PRStream && null != ((PRStream)streamDictionary).Reader) {
				memoryLimitsAwareHandler = ((PRStream)streamDictionary).Reader.GetMemoryLimitsAwareHandler();
			}
			if (null != memoryLimitsAwareHandler) {
				var filterSet = new HashSet2<PdfName>();
				int index;
				for (index = 0; index < filters.Count; index++) {
					var filterName = (PdfName)filters[index];
					if (!filterSet.AddAndCheck(filterName)) {
						memoryLimitsAwareHandler.BeginDecompressedPdfStreamProcessing();
						break;
					}
				}
				if (index == filters.Count) { // The stream isn't suspicious. We shouldn't process it.
					memoryLimitsAwareHandler = null;
				}
			}
			var dp = new List<PdfObject>();
			var dpo = GetPdfObjectRelease(streamDictionary.Get(PdfName.DECODEPARMS));
			if (dpo == null || (!dpo.IsDictionary() && !dpo.IsArray()))
				dpo = GetPdfObjectRelease(streamDictionary.Get(PdfName.DP));
			if (dpo != null) {
				if (dpo.IsDictionary())
					dp.Add(dpo);
				else if (dpo.IsArray())
					dp = ((PdfArray)dpo).ArrayList;
			}
			for (var j = 0; j < filters.Count; ++j) {
				var filterName = (PdfName)filters[j];
				FilterHandlers.IFilterHandler filterHandler;
				filterHandlers.TryGetValue(filterName, out filterHandler);
				if (filterHandler == null)
					throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("the.filter.1.is.not.supported", filterName));

				PdfDictionary decodeParams;
				if (j < dp.Count) {
					var dpEntry = GetPdfObject(dp[j]);
					if (dpEntry is PdfDictionary) {
						decodeParams = (PdfDictionary)dpEntry;
					} else if (dpEntry == null || dpEntry is PdfNull || (dpEntry is PdfLiteral && Util.ArraysAreEqual(Encoding.UTF8.GetBytes("null"), ((PdfLiteral)dpEntry).GetBytes()))) {
						decodeParams = null;
					} else {
						throw new UnsupportedPdfException(MessageLocalization.GetComposedMessage("the.decode.parameter.type.1.is.not.supported", dpEntry.GetType().FullName));
					}

				} else {
					decodeParams = null;
				}
				b = filterHandler.Decode(b, filterName, decodeParams, streamDictionary);
				if (null != memoryLimitsAwareHandler) {
					memoryLimitsAwareHandler.ConsiderBytesOccupiedByDecompressedPdfStream(b.Length);
				}
			}
			if (null != memoryLimitsAwareHandler) {
				memoryLimitsAwareHandler.EndDecompressedPdfStreamProcessing();
			}
			return b;
		}

		/** Get the content from a stream applying the required filters.
		 * @param stream the stream
		 * @param file the location where the stream is
		 * @throws IOException on error
		 * @return the stream content
		 */
		public static byte[] GetStreamBytes(PRStream stream, RandomAccessFileOrArray file) {
			var b = GetStreamBytesRaw(stream, file);
			return DecodeBytes(b, stream);
		}

		/** Get the content from a stream applying the required filters.
		* @param stream the stream
		* @throws IOException on error
		* @return the stream content
		*/
		public static byte[] GetStreamBytes(PRStream stream) {
			var rf = stream.Reader.SafeFile;
			try {
				rf.ReOpen();
				return GetStreamBytes(stream, rf);
			} finally {
				try { rf.Close(); } catch { }
			}
		}

		/** Get the content from a stream as it is without applying any filter.
		* @param stream the stream
		* @param file the location where the stream is
		* @throws IOException on error
		* @return the stream content
		*/
		public static byte[] GetStreamBytesRaw(PRStream stream, RandomAccessFileOrArray file) {
			var reader = stream.Reader;
			byte[] b;
			if (stream.Offset < 0)
				b = stream.GetBytes();
			else {
				b = new byte[stream.Length];
				file.Seek(stream.Offset);
				file.ReadFully(b);
				var decrypt = reader.Decrypt;
				if (decrypt != null) {
					var filter = GetPdfObjectRelease(stream.Get(PdfName.FILTER));
					var filters = new List<PdfObject>();
					if (filter != null) {
						if (filter.IsName())
							filters.Add(filter);
						else if (filter.IsArray())
							filters = ((PdfArray)filter).ArrayList;
					}
					var skip = false;
					for (var k = 0; k < filters.Count; ++k) {
						var obj = GetPdfObjectRelease(filters[k]);
						if (obj != null && obj.ToString().Equals("/Crypt")) {
							skip = true;
							break;
						}
					}
					if (!skip) {
						decrypt.SetHashKey(stream.ObjNum, stream.ObjGen);
						b = decrypt.DecryptByteArray(b);
					}
				}
			}
			return b;
		}

		/** Get the content from a stream as it is without applying any filter.
		* @param stream the stream
		* @throws IOException on error
		* @return the stream content
		*/
		public static byte[] GetStreamBytesRaw(PRStream stream) {
			var rf = stream.Reader.SafeFile;
			try {
				rf.ReOpen();
				return GetStreamBytesRaw(stream, rf);
			} finally {
				try { rf.Close(); } catch { }
			}
		}

		/** Eliminates shared streams if they exist. */
		virtual public void EliminateSharedStreams() {
			if (!sharedStreams)
				return;
			sharedStreams = false;
			if (pageRefs.Size == 1)
				return;
			var newRefs = new List<PRIndirectReference>();
			var newStreams = new List<PRStream>();
			var visited = new IntHashtable();
			for (var k = 1; k <= pageRefs.Size; ++k) {
				var page = pageRefs.GetPageN(k);
				if (page == null)
					continue;
				var contents = GetPdfObject(page.Get(PdfName.CONTENTS));
				if (contents == null)
					continue;
				if (contents.IsStream()) {
					var refi = (PRIndirectReference)page.Get(PdfName.CONTENTS);
					if (visited.ContainsKey(refi.Number)) {
						// need to duplicate
						newRefs.Add(refi);
						newStreams.Add(new PRStream((PRStream)contents, null));
					} else
						visited[refi.Number] = 1;
				} else if (contents.IsArray()) {
					var array = (PdfArray)contents;
					for (var j = 0; j < array.Size; ++j) {
						var refi = (PRIndirectReference)array.GetPdfObject(j);
						if (visited.ContainsKey(refi.Number)) {
							// need to duplicate
							newRefs.Add(refi);
							newStreams.Add(new PRStream((PRStream)GetPdfObject(refi), null));
						} else
							visited[refi.Number] = 1;
					}
				}
			}
			if (newStreams.Count == 0)
				return;
			for (var k = 0; k < newStreams.Count; ++k) {
				xrefObj.Add(newStreams[k]);
				var refi = newRefs[k];
				refi.SetNumber(xrefObj.Count - 1, 0);
			}
		}

		/**
		* Sets the tampered state. A tampered PdfReader cannot be reused in PdfStamper.
		* @param tampered the tampered state
		*/
		virtual public bool Tampered {
			get {
				return tampered;
			}
			set {
				tampered = value;
				pageRefs.KeepPages();
			}
		}

		/** Gets the XML metadata.
		* @throws IOException on error
		* @return the XML metadata
		*/
		virtual public byte[] Metadata {
			get {
				var obj = GetPdfObject(catalog.Get(PdfName.METADATA));
				if (!(obj is PRStream))
					return null;
				var rf = SafeFile;
				byte[] b = null;
				try {
					rf.ReOpen();
					b = GetStreamBytes((PRStream)obj, rf);
				} finally {
					try {
						rf.Close();
					} catch {
						// empty on purpose
					}
				}
				return b;
			}
		}

		/**
		* Gets the byte address of the last xref table.
		* @return the byte address of the last xref table
		*/
		virtual public long LastXref {
			get {
				return lastXref;
			}
		}

		/**
		* Gets the number of xref objects.
		* @return the number of xref objects
		*/
		virtual public int XrefSize {
			get {
				return xrefObj.Count;
			}
		}

		/**
		* Gets the byte address of the %%EOF marker.
		* @return the byte address of the %%EOF marker
		*/
		virtual public long EofPos {
			get {
				return eofPos;
			}
		}

		/**
		* Gets the PDF version. Only the last version char is returned. For example
		* version 1.4 is returned as '4'.
		* @return the PDF version
		*/
		virtual public char PdfVersion {
			get {
				return pdfVersion;
			}
		}

		/**
		* Returns <CODE>true</CODE> if the PDF is encrypted.
		* @return <CODE>true</CODE> if the PDF is encrypted
		*/
		virtual public bool IsEncrypted() {
			return encrypted;
		}

		/**
		* Gets the encryption permissions. It can be used directly in
		* <CODE>PdfWriter.SetEncryption()</CODE>.
		* @return the encryption permissions
		*/
		virtual public long Permissions {
			get {
				return pValue;
			}
		}

		/**
		* Returns <CODE>true</CODE> if the PDF has a 128 bit key encryption.
		* @return <CODE>true</CODE> if the PDF has a 128 bit key encryption
		*/
		virtual public bool Is128Key() {
			return rValue == 3;
		}

		/**
		* Gets the trailer dictionary
		* @return the trailer dictionary
		*/
		virtual public PdfDictionary Trailer {
			get {
				return trailer;
			}
		}

		internal PdfEncryption Decrypt {
			get {
				return decrypt;
			}
		}

		//possible IndexOutException 
		internal static bool Equalsn(byte[] a1, byte[] a2) {
			var length = a2.Length;
			for (var k = 0; k < length; ++k) {
				if (a1[k] != a2[k])
					return false;
			}
			return true;
		}

		internal static bool ExistsName(PdfDictionary dic, PdfName key, PdfName value) {
			var type = GetPdfObjectRelease(dic.Get(key));
			if (type == null || !type.IsName())
				return false;
			var name = (PdfName)type;
			return name.Equals(value);
		}

		internal static String GetFontName(PdfDictionary dic) {
			if (dic == null)
				return null;
			var type = GetPdfObjectRelease(dic.Get(PdfName.BASEFONT));
			if (type == null || !type.IsName())
				return null;
			return PdfName.DecodeName(type.ToString());
		}

		internal static String GetSubsetPrefix(PdfDictionary dic) {
			if (dic == null)
				return null;
			var s = GetFontName(dic);
			if (s == null)
				return null;
			if (s.Length < 8 || s[6] != '+')
				return null;
			for (var k = 0; k < 6; ++k) {
				var c = s[k];
				if (c < 'A' || c > 'Z')
					return null;
			}
			return s;
		}

		/** Finds all the font subsets and changes the prefixes to some
		* random values.
		* @return the number of font subsets altered
		*/
		virtual public int ShuffleSubsetNames() {
			var total = 0;
			for (var k = 1; k < xrefObj.Count; ++k) {
				var obj = GetPdfObjectRelease(k);
				if (obj == null || !obj.IsDictionary())
					continue;
				var dic = (PdfDictionary)obj;
				if (!ExistsName(dic, PdfName.TYPE, PdfName.FONT))
					continue;
				if (ExistsName(dic, PdfName.SUBTYPE, PdfName.TYPE1)
					|| ExistsName(dic, PdfName.SUBTYPE, PdfName.MMTYPE1)
					|| ExistsName(dic, PdfName.SUBTYPE, PdfName.TRUETYPE)) {
					var s = GetSubsetPrefix(dic);
					if (s == null)
						continue;
					var ns = BaseFont.CreateSubsetPrefix() + s.Substring(7);
					var newName = new PdfName(ns);
					dic.Put(PdfName.BASEFONT, newName);
					SetXrefPartialObject(k, dic);
					++total;
					var fd = dic.GetAsDict(PdfName.FONTDESCRIPTOR);
					if (fd == null)
						continue;
					fd.Put(PdfName.FONTNAME, newName);
				} else if (ExistsName(dic, PdfName.SUBTYPE, PdfName.TYPE0)) {
					var s = GetSubsetPrefix(dic);
					var arr = dic.GetAsArray(PdfName.DESCENDANTFONTS);
					if (arr == null)
						continue;
					if (arr.IsEmpty())
						continue;
					var desc = arr.GetAsDict(0);
					var sde = GetSubsetPrefix(desc);
					if (sde == null)
						continue;
					var ns = BaseFont.CreateSubsetPrefix();
					if (s != null)
						dic.Put(PdfName.BASEFONT, new PdfName(ns + s.Substring(7)));
					SetXrefPartialObject(k, dic);
					var newName = new PdfName(ns + sde.Substring(7));
					desc.Put(PdfName.BASEFONT, newName);
					++total;
					var fd = desc.GetAsDict(PdfName.FONTDESCRIPTOR);
					if (fd == null)
						continue;
					fd.Put(PdfName.FONTNAME, newName);
				}
			}
			return total;
		}

		/** Finds all the fonts not subset but embedded and marks them as subset.
		* @return the number of fonts altered
		*/
		virtual public int CreateFakeFontSubsets() {
			var total = 0;
			for (var k = 1; k < xrefObj.Count; ++k) {
				var obj = GetPdfObjectRelease(k);
				if (obj == null || !obj.IsDictionary())
					continue;
				var dic = (PdfDictionary)obj;
				if (!ExistsName(dic, PdfName.TYPE, PdfName.FONT))
					continue;
				if (ExistsName(dic, PdfName.SUBTYPE, PdfName.TYPE1)
					|| ExistsName(dic, PdfName.SUBTYPE, PdfName.MMTYPE1)
					|| ExistsName(dic, PdfName.SUBTYPE, PdfName.TRUETYPE)) {
					var s = GetSubsetPrefix(dic);
					if (s != null)
						continue;
					s = GetFontName(dic);
					if (s == null)
						continue;
					var ns = BaseFont.CreateSubsetPrefix() + s;
					var fd = (PdfDictionary)GetPdfObjectRelease(dic.Get(PdfName.FONTDESCRIPTOR));
					if (fd == null)
						continue;
					if (fd.Get(PdfName.FONTFILE) == null && fd.Get(PdfName.FONTFILE2) == null
						&& fd.Get(PdfName.FONTFILE3) == null)
						continue;
					fd = dic.GetAsDict(PdfName.FONTDESCRIPTOR);
					var newName = new PdfName(ns);
					dic.Put(PdfName.BASEFONT, newName);
					fd.Put(PdfName.FONTNAME, newName);
					SetXrefPartialObject(k, dic);
					++total;
				}
			}
			return total;
		}

		private static PdfArray GetNameArray(PdfObject obj) {
			if (obj == null)
				return null;
			obj = GetPdfObjectRelease(obj);
			if (obj == null)
				return null;
			if (obj.IsArray())
				return (PdfArray)obj;
			else if (obj.IsDictionary()) {
				var arr2 = GetPdfObjectRelease(((PdfDictionary)obj).Get(PdfName.D));
				if (arr2 != null && arr2.IsArray())
					return (PdfArray)arr2;
			}
			return null;
		}

		/**
		* Gets all the named destinations as an <CODE>Hashtable</CODE>. The key is the name
		* and the value is the destinations array.
		* @return gets all the named destinations
		*/
		virtual public Dictionary<Object, PdfObject> GetNamedDestination() {
			return GetNamedDestination(false);
		}

		/**
		* Gets all the named destinations as an <CODE>HashMap</CODE>. The key is the name
		* and the value is the destinations array.
		* @param   keepNames   true if you want the keys to be real PdfNames instead of Strings
		* @return gets all the named destinations
		* @since   2.1.6
		*/
		virtual public Dictionary<Object, PdfObject> GetNamedDestination(bool keepNames) {
			var names = GetNamedDestinationFromNames(keepNames);
			var names2 = GetNamedDestinationFromStrings();
			foreach (var ie in names2)
				names[ie.Key] = ie.Value;
			return names;
		}

		/**
		* Gets the named destinations from the /Dests key in the catalog as an <CODE>Hashtable</CODE>. The key is the name
		* and the value is the destinations array.
		* @return gets the named destinations
		*/
		virtual public Dictionary<String, PdfObject> GetNamedDestinationFromNames() {
			var ret = new Dictionary<string, PdfObject>();
			foreach (var s in GetNamedDestinationFromNames(false))
				ret[(string)s.Key] = s.Value;
			return ret;
		}

		/**
		* Gets the named destinations from the /Dests key in the catalog as an <CODE>HashMap</CODE>. The key is the name
		* and the value is the destinations array.
		* @param   keepNames   true if you want the keys to be real PdfNames instead of Strings
		* @return gets the named destinations
		* @since   2.1.6
		*/
		virtual public Dictionary<Object, PdfObject> GetNamedDestinationFromNames(bool keepNames) {
			var names = new Dictionary<Object, PdfObject>();
			if (catalog.Get(PdfName.DESTS) != null) {
				var dic = (PdfDictionary)GetPdfObjectRelease(catalog.Get(PdfName.DESTS));
				if (dic == null)
					return names;
				foreach (var key in dic.Keys) {
					var arr = GetNameArray(dic.Get(key));
					if (arr == null)
						continue;
					if (keepNames) {
						names[key] = arr;
					} else {
						var name = PdfName.DecodeName(key.ToString());
						names[name] = arr;
					}
				}
			}
			return names;
		}

		/**
		* Gets the named destinations from the /Names key in the catalog as an <CODE>Hashtable</CODE>. The key is the name
		* and the value is the destinations array.
		* @return gets the named destinations
		*/
		virtual public Dictionary<String, PdfObject> GetNamedDestinationFromStrings() {
			if (catalog.Get(PdfName.NAMES) != null) {
				var dic = (PdfDictionary)GetPdfObjectRelease(catalog.Get(PdfName.NAMES));
				if (dic != null) {
					dic = (PdfDictionary)GetPdfObjectRelease(dic.Get(PdfName.DESTS));
					if (dic != null) {
						var names = PdfNameTree.ReadTree(dic);
						var keys = new string[names.Count];
						names.Keys.CopyTo(keys, 0);
						foreach (var key in keys) {
							var arr = GetNameArray(names[key]);
							if (arr != null)
								names[key] = arr;
							else
								names.Remove(key);
						}
						return names;
					}
				}
			}
			return new Dictionary<String, PdfObject>();
		}

		/**
		* Removes all the fields from the document.
		*/
		virtual public void RemoveFields() {
			pageRefs.ResetReleasePage();
			for (var k = 1; k <= pageRefs.Size; ++k) {
				var page = pageRefs.GetPageN(k);
				var annots = page.GetAsArray(PdfName.ANNOTS);
				if (annots == null) {
					pageRefs.ReleasePage(k);
					continue;
				}
				for (var j = 0; j < annots.Size; ++j) {
					var obj = GetPdfObjectRelease((PdfObject)annots.GetPdfObject(j));
					if (obj == null || !obj.IsDictionary())
						continue;
					var annot = (PdfDictionary)obj;
					if (PdfName.WIDGET.Equals(annot.Get(PdfName.SUBTYPE)))
						annots.Remove(j--);
				}
				if (annots.IsEmpty())
					page.Remove(PdfName.ANNOTS);
				else
					pageRefs.ReleasePage(k);
			}
			catalog.Remove(PdfName.ACROFORM);
			pageRefs.ResetReleasePage();
		}

		/**
		* Removes all the annotations and fields from the document.
		*/
		virtual public void RemoveAnnotations() {
			pageRefs.ResetReleasePage();
			for (var k = 1; k <= pageRefs.Size; ++k) {
				var page = pageRefs.GetPageN(k);
				if (page.Get(PdfName.ANNOTS) == null)
					pageRefs.ReleasePage(k);
				else
					page.Remove(PdfName.ANNOTS);
			}
			catalog.Remove(PdfName.ACROFORM);
			pageRefs.ResetReleasePage();
		}

		virtual public List<PdfAnnotation.PdfImportedLink> GetLinks(int page) {
			pageRefs.ResetReleasePage();
			var result = new List<PdfAnnotation.PdfImportedLink>();
			var pageDic = pageRefs.GetPageN(page);
			if (pageDic.Get(PdfName.ANNOTS) != null) {
				var annots = pageDic.GetAsArray(PdfName.ANNOTS);
				for (var j = 0; j < annots.Size; ++j) {
					var annot = (PdfDictionary)GetPdfObjectRelease(annots.GetPdfObject(j));

					if (PdfName.LINK.Equals(annot.Get(PdfName.SUBTYPE))) {
						result.Add(new PdfAnnotation.PdfImportedLink(annot));
					}
				}
			}
			pageRefs.ReleasePage(page);
			pageRefs.ResetReleasePage();
			return result;
		}

		private void IterateBookmarks(PdfObject outlineRef, Dictionary<Object, PdfObject> names) {
			while (outlineRef != null) {
				ReplaceNamedDestination(outlineRef, names);
				var outline = (PdfDictionary)GetPdfObjectRelease(outlineRef);
				var first = outline.Get(PdfName.FIRST);
				if (first != null) {
					IterateBookmarks(first, names);
				}
				outlineRef = outline.Get(PdfName.NEXT);
			}
		}

		/**
		* Replaces remote named links with local destinations that have the same name.
		* @since   5.0
		*/
		virtual public void MakeRemoteNamedDestinationsLocal() {
			if (remoteToLocalNamedDestinations)
				return;
			remoteToLocalNamedDestinations = true;
			var names = GetNamedDestination(true);
			if (names.Count == 0)
				return;
			for (var k = 1; k <= pageRefs.Size; ++k) {
				var page = pageRefs.GetPageN(k);
				PdfObject annotsRef;
				var annots = (PdfArray)GetPdfObject(annotsRef = page.Get(PdfName.ANNOTS));
				var annotIdx = lastXrefPartial;
				ReleaseLastXrefPartial();
				if (annots == null) {
					pageRefs.ReleasePage(k);
					continue;
				}
				var commitAnnots = false;
				for (var an = 0; an < annots.Size; ++an) {
					var objRef = annots.GetPdfObject(an);
					if (ConvertNamedDestination(objRef, names) && !objRef.IsIndirect())
						commitAnnots = true;
				}
				if (commitAnnots)
					SetXrefPartialObject(annotIdx, annots);
				if (!commitAnnots || annotsRef.IsIndirect())
					pageRefs.ReleasePage(k);
			}
		}

		/**
		* Converts a remote named destination GoToR with a local named destination
		* if there's a corresponding name.
		* @param   obj an annotation that needs to be screened for links to external named destinations.
		* @param   names   a map with names of local named destinations
		* @since   iText 5.0
		*/
		private bool ConvertNamedDestination(PdfObject obj, Dictionary<Object, PdfObject> names) {
			obj = GetPdfObject(obj);
			var objIdx = lastXrefPartial;
			ReleaseLastXrefPartial();
			if (obj != null && obj.IsDictionary()) {
				var ob2 = GetPdfObject(((PdfDictionary)obj).Get(PdfName.A));
				if (ob2 != null) {
					var obj2Idx = lastXrefPartial;
					ReleaseLastXrefPartial();
					var dic = (PdfDictionary)ob2;
					var type = (PdfName)GetPdfObjectRelease(dic.Get(PdfName.S));
					if (PdfName.GOTOR.Equals(type)) {
						var ob3 = GetPdfObjectRelease(dic.Get(PdfName.D));
						Object name = null;
						if (ob3 != null) {
							if (ob3.IsName())
								name = ob3;
							else if (ob3.IsString())
								name = ob3.ToString();
							PdfArray dest = null;
							if (name != null && names.ContainsKey(name))
								dest = (PdfArray)names[name];
							if (dest != null) {
								dic.Remove(PdfName.F);
								dic.Remove(PdfName.NEWWINDOW);
								dic.Put(PdfName.S, PdfName.GOTO);
								SetXrefPartialObject(obj2Idx, ob2);
								SetXrefPartialObject(objIdx, obj);
								return true;
							}
						}
					}
				}
			}
			return false;
		}

		/** Replaces all the local named links with the actual destinations. */
		virtual public void ConsolidateNamedDestinations() {
			if (consolidateNamedDestinations)
				return;
			consolidateNamedDestinations = true;
			var names = GetNamedDestination(true);
			if (names.Count == 0)
				return;
			for (var k = 1; k <= pageRefs.Size; ++k) {
				var page = pageRefs.GetPageN(k);
				PdfObject annotsRef;
				var annots = (PdfArray)GetPdfObject(annotsRef = page.Get(PdfName.ANNOTS));
				var annotIdx = lastXrefPartial;
				ReleaseLastXrefPartial();
				if (annots == null) {
					pageRefs.ReleasePage(k);
					continue;
				}
				var commitAnnots = false;
				for (var an = 0; an < annots.Size; ++an) {
					var objRef = annots.GetPdfObject(an);
					if (ReplaceNamedDestination(objRef, names) && !objRef.IsIndirect())
						commitAnnots = true;
				}
				if (commitAnnots)
					SetXrefPartialObject(annotIdx, annots);
				if (!commitAnnots || annotsRef.IsIndirect())
					pageRefs.ReleasePage(k);
			}
			var outlines = (PdfDictionary)GetPdfObjectRelease(catalog.Get(PdfName.OUTLINES));
			if (outlines == null)
				return;
			IterateBookmarks(outlines.Get(PdfName.FIRST), names);
		}

		private bool ReplaceNamedDestination(PdfObject obj, Dictionary<Object, PdfObject> names) {
			obj = GetPdfObject(obj);
			var objIdx = lastXrefPartial;
			ReleaseLastXrefPartial();
			if (obj != null && obj.IsDictionary()) {
				var ob2 = GetPdfObjectRelease(((PdfDictionary)obj).Get(PdfName.DEST));
				Object name = null;
				if (ob2 != null) {
					if (ob2.IsName())
						name = ob2;
					else if (ob2.IsString())
						name = ob2.ToString();
					if (name != null) {
						PdfArray dest = null;
						if (names.ContainsKey(name) && names[name] is PdfArray)
							dest = (PdfArray)names[name];
						if (dest != null) {
							((PdfDictionary)obj).Put(PdfName.DEST, dest);
							SetXrefPartialObject(objIdx, obj);
							return true;
						}
					}
				} else if ((ob2 = GetPdfObject(((PdfDictionary)obj).Get(PdfName.A))) != null) {
					var obj2Idx = lastXrefPartial;
					ReleaseLastXrefPartial();
					var dic = (PdfDictionary)ob2;
					var type = (PdfName)GetPdfObjectRelease(dic.Get(PdfName.S));
					if (PdfName.GOTO.Equals(type)) {
						var ob3 = GetPdfObjectRelease(dic.Get(PdfName.D));
						if (ob3 != null) {
							if (ob3.IsName())
								name = ob3;
							else if (ob3.IsString())
								name = ob3.ToString();
						}
						if (name != null) {
							PdfArray dest = null;
							if (names.ContainsKey(name) && names[name] is PdfArray)
								dest = (PdfArray)names[name];
							if (dest != null) {
								dic.Put(PdfName.D, dest);
								SetXrefPartialObject(obj2Idx, ob2);
								SetXrefPartialObject(objIdx, obj);
								return true;
							}
						}
					}
				}
			}
			return false;
		}

		protected internal static PdfDictionary DuplicatePdfDictionary(PdfDictionary original, PdfDictionary copy, PdfReader newReader) {
			if (copy == null)
				copy = new PdfDictionary(original.Size);
			foreach (var key in original.Keys) {
				copy.Put(key, DuplicatePdfObject(original.Get(key), newReader));
			}
			return copy;
		}

		protected internal static PdfObject DuplicatePdfObject(PdfObject original, PdfReader newReader) {
			if (original == null)
				return null;
			switch (original.Type) {
				case PdfObject.DICTIONARY: {
					return DuplicatePdfDictionary((PdfDictionary)original, null, newReader);
				}
				case PdfObject.STREAM: {
					var org = (PRStream)original;
					var stream = new PRStream(org, null, newReader);
					DuplicatePdfDictionary(org, stream, newReader);
					return stream;
				}
				case PdfObject.ARRAY: {
					var originalArray = (PdfArray)original;
					var arr = new PdfArray(originalArray.Size);
					for (var it = originalArray.GetListIterator(); it.HasNext();) {
						arr.Add(DuplicatePdfObject((PdfObject)it.Next(), newReader));
					}
					return arr;
				}
				case PdfObject.INDIRECT: {
					var org = (PRIndirectReference)original;
					return new PRIndirectReference(newReader, org.Number, org.Generation);
				}
				default:
					return original;
			}
		}

		/**
		* Closes the reader, and any underlying stream or data source used to create the reader
		*/
		virtual public void Close() {
			tokens.Close();
		}

		virtual protected internal void RemoveUnusedNode(PdfObject obj, bool[] hits) {
			var state = new Stack<object>();
			state.Push(obj);
			while (state.Count != 0) {
				var current = state.Pop();
				if (current == null)
					continue;
				List<PdfObject> ar = null;
				PdfDictionary dic = null;
				PdfName[] keys = null;
				Object[] objs = null;
				var idx = 0;
				if (current is PdfObject) {
					obj = (PdfObject)current;
					switch (obj.Type) {
						case PdfObject.DICTIONARY:
						case PdfObject.STREAM:
							dic = (PdfDictionary)obj;
							keys = new PdfName[dic.Size];
							dic.Keys.CopyTo(keys, 0);
							break;
						case PdfObject.ARRAY:
							ar = ((PdfArray)obj).ArrayList;
							break;
						case PdfObject.INDIRECT:
							var refi = (PRIndirectReference)obj;
							var num = refi.Number;
							if (!hits[num]) {
								hits[num] = true;
								state.Push(GetPdfObjectRelease(refi));
							}
							continue;
						default:
							continue;
					}
				} else {
					objs = (Object[])current;
					if (objs[0] is List<PdfObject>) {
						ar = (List<PdfObject>)objs[0];
						idx = (int)objs[1];
					} else {
						keys = (PdfName[])objs[0];
						dic = (PdfDictionary)objs[1];
						idx = (int)objs[2];
					}
				}
				if (ar != null) {
					for (var k = idx; k < ar.Count; ++k) {
						var v = ar[k];
						if (v.IsIndirect()) {
							var num = ((PRIndirectReference)v).Number;
							if (num >= xrefObj.Count || (!partial && xrefObj[num] == null)) {
								ar[k] = PdfNull.PDFNULL;
								continue;
							}
						}
						if (objs == null)
							state.Push(new Object[] { ar, k + 1 });
						else {
							objs[1] = k + 1;
							state.Push(objs);
						}
						state.Push(v);
						break;
					}
				} else {
					for (var k = idx; k < keys.Length; ++k) {
						var key = keys[k];
						var v = dic.Get(key);
						if (v.IsIndirect()) {
							var num = ((PRIndirectReference)v).Number;
							if (num < 0 || num >= xrefObj.Count || (!partial && xrefObj[num] == null)) {
								dic.Put(key, PdfNull.PDFNULL);
								continue;
							}
						}
						if (objs == null)
							state.Push(new Object[] { keys, dic, k + 1 });
						else {
							objs[2] = k + 1;
							state.Push(objs);
						}
						state.Push(v);
						break;
					}
				}
			}
		}

		/** Removes all the unreachable objects.
		* @return the number of indirect objects removed
		*/
		virtual public int RemoveUnusedObjects() {
			var hits = new bool[xrefObj.Count];
			RemoveUnusedNode(trailer, hits);
			var total = 0;
			if (partial) {
				for (var k = 1; k < hits.Length; ++k) {
					if (!hits[k]) {
						xref[k * 2] = -1;
						xref[k * 2 + 1] = 0;
						xrefObj[k] = null;
						++total;
					}
				}
			} else {
				for (var k = 1; k < hits.Length; ++k) {
					if (!hits[k]) {
						xrefObj[k] = null;
						++total;
					}
				}
			}
			return total;
		}

		/** Gets a read-only version of <CODE>AcroFields</CODE>.
		* @return a read-only version of <CODE>AcroFields</CODE>
		*/
		virtual public AcroFields AcroFields {
			get {
				return new AcroFields(this, null);
			}
		}

		/**
		* Gets the global document JavaScript.
		* @param file the document file
		* @throws IOException on error
		* @return the global document JavaScript
		*/
		virtual public String GetJavaScript(RandomAccessFileOrArray file) {
			var names = (PdfDictionary)GetPdfObjectRelease(catalog.Get(PdfName.NAMES));
			if (names == null)
				return null;
			var js = (PdfDictionary)GetPdfObjectRelease(names.Get(PdfName.JAVASCRIPT));
			if (js == null)
				return null;
			var jscript = PdfNameTree.ReadTree(js);
			var sortedNames = new String[jscript.Count];
			jscript.Keys.CopyTo(sortedNames, 0);
			Array.Sort(sortedNames);
			var buf = new StringBuilder();
			for (var k = 0; k < sortedNames.Length; ++k) {
				var j = (PdfDictionary)GetPdfObjectRelease(jscript[sortedNames[k]]);
				if (j == null)
					continue;
				var obj = GetPdfObjectRelease(j.Get(PdfName.JS));
				if (obj != null) {
					if (obj.IsString())
						buf.Append(((PdfString)obj).ToUnicodeString()).Append('\n');
					else if (obj.IsStream()) {
						var bytes = GetStreamBytes((PRStream)obj, file);
						if (bytes.Length >= 2 && bytes[0] == (byte)254 && bytes[1] == (byte)255)
							buf.Append(PdfEncodings.ConvertToString(bytes, PdfObject.TEXT_UNICODE));
						else
							buf.Append(PdfEncodings.ConvertToString(bytes, PdfObject.TEXT_PDFDOCENCODING));
						buf.Append('\n');
					}
				}
			}
			return buf.ToString();
		}

		/**
		* Gets the global document JavaScript.
		* @throws IOException on error
		* @return the global document JavaScript
		*/
		virtual public String JavaScript {
			get {
				var rf = SafeFile;
				try {
					rf.ReOpen();
					return GetJavaScript(rf);
				} finally {
					try { rf.Close(); } catch { }
				}
			}
		}

		/**
		* Selects the pages to keep in the document. The pages are described as
		* ranges. The page ordering can be changed but
		* no page repetitions are allowed. Note that it may be very slow in partial mode.
		* @param ranges the comma separated ranges as described in {@link SequenceList}
		*/
		virtual public void SelectPages(String ranges) {
			SelectPages(SequenceList.Expand(ranges, NumberOfPages));
		}

		/**
		* Selects the pages to keep in the document. The pages are described as a
		* <CODE>List</CODE> of <CODE>Integer</CODE>. The page ordering can be changed but
		* no page repetitions are allowed. Note that it may be very slow in partial mode.
		* @param pagesToKeep the pages to keep in the document
		*/
		virtual public void SelectPages(ICollection<int> pagesToKeep) {
			SelectPages(pagesToKeep, true);
		}

		/**
		 * Selects the pages to keep in the document. The pages are described as a
		 * <CODE>List</CODE> of <CODE>Integer</CODE>. The page ordering can be changed but
		 * no page repetitions are allowed. Note that it may be very slow in partial mode.
		 * @param pagesToKeep the pages to keep in the document
		 * @param removeUnused indicate if to remove unsed objects. @see removeUnusedObjects
		 */
		internal void SelectPages(ICollection<int> pagesToKeep, bool removeUnused) {
			pageRefs.SelectPages(pagesToKeep);
			if (removeUnused) RemoveUnusedObjects();
		}

		/** Sets the viewer preferences as the sum of several constants.
		* @param preferences the viewer preferences
		* @see PdfViewerPreferences#setViewerPreferences
		*/
		public virtual int ViewerPreferences {
			set {
				this.viewerPreferences.ViewerPreferences = value;
				SetViewerPreferences(this.viewerPreferences);
			}
		}

		/** Adds a viewer preference
		* @param key a key for a viewer preference
		* @param value a value for the viewer preference
		* @see PdfViewerPreferences#addViewerPreference
		*/
		public virtual void AddViewerPreference(PdfName key, PdfObject value) {
			this.viewerPreferences.AddViewerPreference(key, value);
			SetViewerPreferences(this.viewerPreferences);
		}

		public virtual void SetViewerPreferences(PdfViewerPreferencesImp vp) {
			vp.AddToCatalog(catalog);
		}

		/**
		* Returns a bitset representing the PageMode and PageLayout viewer preferences.
		* Doesn't return any information about the ViewerPreferences dictionary.
		* @return an int that contains the Viewer Preferences.
		*/
		public virtual int SimpleViewerPreferences {
			get {
				return PdfViewerPreferencesImp.GetViewerPreferences(catalog).PageLayoutAndMode;
			}
		}

		virtual public bool Appendable {
			set {
				appendable = value;
				if (appendable)
					GetPdfObject(trailer.Get(PdfName.ROOT));
			}
			get {
				return appendable;
			}
		}

		/**
		* Getter for property newXrefType.
		* @return Value of property newXrefType.
		*/
		virtual public bool IsNewXrefType() {
			return newXrefType;
		}

		/**
		* Getter for property fileLength.
		* @return Value of property fileLength.
		*/
		virtual public long FileLength {
			get {
				return fileLength;
			}
		}

		/**
		* Getter for property hybridXref.
		* @return Value of property hybridXref.
		*/
		virtual public bool IsHybridXref() {
			return hybridXref;
		}

		public class PageRefs {
			private PdfReader reader;
			private IntHashtable refsp;
			private List<PRIndirectReference> refsn;
			private List<PdfDictionary> pageInh;
			private int lastPageRead = -1;
			private int sizep;
			private bool keepPages;
			/**
			* Keeps track of all pages nodes to avoid circular references.
			*/
			private HashSet2<PdfObject> pagesNodes = new HashSet2<PdfObject>();

			internal PageRefs(PdfReader reader) {
				this.reader = reader;
				if (reader.partial) {
					refsp = new IntHashtable();
					var npages = (PdfNumber)PdfReader.GetPdfObjectRelease(reader.rootPages.Get(PdfName.COUNT));
					sizep = npages.IntValue;
				} else {
					ReadPages();
				}
			}

			internal PageRefs(PageRefs other, PdfReader reader) {
				this.reader = reader;
				this.sizep = other.sizep;
				if (other.refsn != null) {
					refsn = new List<PRIndirectReference>(other.refsn);
					for (var k = 0; k < refsn.Count; ++k) {
						refsn[k] = (PRIndirectReference)DuplicatePdfObject(refsn[k], reader);
					}
				} else
					this.refsp = (IntHashtable)other.refsp.Clone();
			}

			internal int Size {
				get {
					if (refsn != null)
						return refsn.Count;
					else
						return sizep;
				}
			}

			internal void ReadPages() {
				if (refsn != null)
					return;
				refsp = null;
				refsn = new List<PRIndirectReference>();
				pageInh = new List<PdfDictionary>();
				IteratePages((PRIndirectReference)reader.catalog.Get(PdfName.PAGES));
				pageInh = null;
				reader.rootPages.Put(PdfName.COUNT, new PdfNumber(refsn.Count));
			}

			internal void ReReadPages() {
				refsn = null;
				ReadPages();
			}

			/** Gets the dictionary that represents a page.
			* @param pageNum the page number. 1 is the first
			* @return the page dictionary
			*/
			virtual public PdfDictionary GetPageN(int pageNum) {
				var refi = GetPageOrigRef(pageNum);
				return (PdfDictionary)PdfReader.GetPdfObject(refi);
			}

			/**
			* @param pageNum
			* @return a dictionary object
			*/
			virtual public PdfDictionary GetPageNRelease(int pageNum) {
				var page = GetPageN(pageNum);
				ReleasePage(pageNum);
				return page;
			}

			/**
			* @param pageNum
			* @return an indirect reference
			*/
			virtual public PRIndirectReference GetPageOrigRefRelease(int pageNum) {
				var refi = GetPageOrigRef(pageNum);
				ReleasePage(pageNum);
				return refi;
			}

			/** Gets the page reference to this page.
			* @param pageNum the page number. 1 is the first
			* @return the page reference
			*/
			virtual public PRIndirectReference GetPageOrigRef(int pageNum) {
				--pageNum;
				if (pageNum < 0 || pageNum >= Size)
					return null;
				if (refsn != null)
					return refsn[pageNum];
				else {
					var n = refsp[pageNum];
					if (n == 0) {
						var refi = GetSinglePage(pageNum);
						if (reader.lastXrefPartial == -1)
							lastPageRead = -1;
						else
							lastPageRead = pageNum;
						reader.lastXrefPartial = -1;
						refsp[pageNum] = refi.Number;
						if (keepPages)
							lastPageRead = -1;
						return refi;
					} else {
						if (lastPageRead != pageNum)
							lastPageRead = -1;
						if (keepPages)
							lastPageRead = -1;
						return new PRIndirectReference(reader, n);
					}
				}
			}

			internal void KeepPages() {
				if (refsp == null || keepPages)
					return;
				keepPages = true;
				refsp.Clear();
			}

			/**
			* @param pageNum
			*/
			virtual public void ReleasePage(int pageNum) {
				if (refsp == null)
					return;
				--pageNum;
				if (pageNum < 0 || pageNum >= Size)
					return;
				if (pageNum != lastPageRead)
					return;
				lastPageRead = -1;
				reader.lastXrefPartial = refsp[pageNum];
				reader.ReleaseLastXrefPartial();
				refsp.Remove(pageNum);
			}

			/**
			* 
			*/
			virtual public void ResetReleasePage() {
				if (refsp == null)
					return;
				lastPageRead = -1;
			}

			internal void InsertPage(int pageNum, PRIndirectReference refi) {
				--pageNum;
				if (refsn != null) {
					if (pageNum >= refsn.Count)
						refsn.Add(refi);
					else
						refsn.Insert(pageNum, refi);
				} else {
					++sizep;
					lastPageRead = -1;
					if (pageNum >= Size) {
						refsp[Size] = refi.Number;
					} else {
						var refs2 = new IntHashtable((refsp.Size + 1) * 2);
						for (var it = refsp.GetEntryIterator(); it.HasNext();) {
							var entry = (IntHashtable.IntHashtableEntry)it.Next();
							var p = entry.Key;
							refs2[p >= pageNum ? p + 1 : p] = entry.Value;
						}
						refs2[pageNum] = refi.Number;
						refsp = refs2;
					}
				}
			}

			private void PushPageAttributes(PdfDictionary nodePages) {
				var dic = new PdfDictionary();
				if (pageInh.Count != 0) {
					dic.Merge(pageInh[pageInh.Count - 1]);
				}
				for (var k = 0; k < pageInhCandidates.Length; ++k) {
					var obj = nodePages.Get(pageInhCandidates[k]);
					if (obj != null)
						dic.Put(pageInhCandidates[k], obj);
				}
				pageInh.Add(dic);
			}

			private void PopPageAttributes() {
				pageInh.RemoveAt(pageInh.Count - 1);
			}

			private void IteratePages(PRIndirectReference rpage) {

				var page = (PdfDictionary)GetPdfObject(rpage);
				if (page == null)
					return;
				if (!pagesNodes.AddAndCheck(page))
					throw new InvalidPdfException(MessageLocalization.GetComposedMessage("illegal.pages.tree"));

				var kidsPR = page.GetAsArray(PdfName.KIDS);
				if (kidsPR == null) {
					page.Put(PdfName.TYPE, PdfName.PAGE);
					var dic = pageInh[pageInh.Count - 1];
					foreach (var key in dic.Keys) {
						if (page.Get(key) == null)
							page.Put(key, dic.Get(key));
					}
					if (page.Get(PdfName.MEDIABOX) == null) {
						var arr = new PdfArray(new float[] { 0, 0, PageSize.LETTER.Right, PageSize.LETTER.Top });
						page.Put(PdfName.MEDIABOX, arr);
					}
					refsn.Add(rpage);
				} else {
					page.Put(PdfName.TYPE, PdfName.PAGES);
					PushPageAttributes(page);
					for (var k = 0; k < kidsPR.Size; ++k) {
						var obj = kidsPR.GetPdfObject(k);
						if (!obj.IsIndirect()) {
							while (k < kidsPR.Size)
								kidsPR.Remove(k);
							break;
						}
						IteratePages((PRIndirectReference)obj);
					}
					PopPageAttributes();
				}
			}

			virtual protected internal PRIndirectReference GetSinglePage(int n) {
				var acc = new PdfDictionary();
				var top = reader.rootPages;
				var baseb = 0;
				while (true) {
					for (var k = 0; k < pageInhCandidates.Length; ++k) {
						var obj = top.Get(pageInhCandidates[k]);
						if (obj != null)
							acc.Put(pageInhCandidates[k], obj);
					}
					var kids = (PdfArray)PdfReader.GetPdfObjectRelease(top.Get(PdfName.KIDS));
					for (var it = new ListIterator<PdfObject>(kids.ArrayList); it.HasNext();) {
						var refi = (PRIndirectReference)it.Next();
						var dic = (PdfDictionary)GetPdfObject(refi);
						var last = reader.lastXrefPartial;
						var count = GetPdfObjectRelease(dic.Get(PdfName.COUNT));
						reader.lastXrefPartial = last;
						var acn = 1;
						if (count != null && count.Type == PdfObject.NUMBER)
							acn = ((PdfNumber)count).IntValue;
						if (n < baseb + acn) {
							if (count == null) {
								dic.MergeDifferent(acc);
								return refi;
							}
							reader.ReleaseLastXrefPartial();
							top = dic;
							break;
						}
						reader.ReleaseLastXrefPartial();
						baseb += acn;
					}
				}
			}

			internal void SelectPages(ICollection<int> pagesToKeep) {
				var pg = new IntHashtable();
				var finalPages = new List<int>();
				var psize = Size;
				foreach (var p in pagesToKeep) {
					if (p >= 1 && p <= psize && !pg.ContainsKey(p)) {
						pg[p] = 1;
						finalPages.Add(p);
					}
				}
				if (reader.partial) {
					for (var k = 1; k <= psize; ++k) {
						GetPageOrigRef(k);
						ResetReleasePage();
					}
				}
				var parent = (PRIndirectReference)reader.catalog.Get(PdfName.PAGES);
				var topPages = (PdfDictionary)PdfReader.GetPdfObject(parent);
				var newPageRefs = new List<PRIndirectReference>(finalPages.Count);
				var kids = new PdfArray();
				foreach (var p in finalPages) {
					var pref = GetPageOrigRef(p);
					ResetReleasePage();
					kids.Add(pref);
					newPageRefs.Add(pref);
					GetPageN(p).Put(PdfName.PARENT, parent);
				}
				var af = reader.AcroFields;
				var removeFields = (af.Fields.Count > 0);
				for (var k = 1; k <= psize; ++k) {
					if (!pg.ContainsKey(k)) {
						if (removeFields)
							af.RemoveFieldsFromPage(k);
						var pref = GetPageOrigRef(k);
						var nref = pref.Number;
						reader.xrefObj[nref] = null;
						if (reader.partial) {
							reader.xref[nref * 2] = -1;
							reader.xref[nref * 2 + 1] = 0;
						}
					}
				}
				topPages.Put(PdfName.COUNT, new PdfNumber(finalPages.Count));
				topPages.Put(PdfName.KIDS, kids);
				refsp = null;
				refsn = newPageRefs;
			}
		}

		internal PdfIndirectReference GetCryptoRef() {
			if (cryptoRef == null)
				return null;
			return new PdfIndirectReference(0, cryptoRef.Number, cryptoRef.Generation);
		}

		/**
		 * Checks if this PDF has usage rights enabled.
		 *
		 * @return <code>true</code> if usage rights are present; <code>false</code> otherwise
		 */
		virtual public bool HasUsageRights() {
			var perms = catalog.GetAsDict(PdfName.PERMS);
			if (perms == null)
				return false;
			return perms.Contains(PdfName.UR) || perms.Contains(PdfName.UR3);
		}

		/**
		* Removes any usage rights that this PDF may have. Only Adobe can grant usage rights
		* and any PDF modification with iText will invalidate them. Invalidated usage rights may
		* confuse Acrobat and it's advisabe to remove them altogether.
		*/
		virtual public void RemoveUsageRights() {
			var perms = catalog.GetAsDict(PdfName.PERMS);
			if (perms == null)
				return;
			perms.Remove(PdfName.UR);
			perms.Remove(PdfName.UR3);
			if (perms.Size == 0)
				catalog.Remove(PdfName.PERMS);
		}

		/**
		* Gets the certification level for this document. The return values can be <code>PdfSignatureAppearance.NOT_CERTIFIED</code>, 
		* <code>PdfSignatureAppearance.CERTIFIED_NO_CHANGES_ALLOWED</code>,
		* <code>PdfSignatureAppearance.CERTIFIED_FORM_FILLING</code> and
		* <code>PdfSignatureAppearance.CERTIFIED_FORM_FILLING_AND_ANNOTATIONS</code>.
		* <p>
		* No signature validation is made, use the methods availabe for that in <CODE>AcroFields</CODE>.
		* </p>
		* @return gets the certification level for this document
		*/
		virtual public int GetCertificationLevel() {
			var dic = catalog.GetAsDict(PdfName.PERMS);
			if (dic == null)
				return PdfSignatureAppearance.NOT_CERTIFIED;
			dic = dic.GetAsDict(PdfName.DOCMDP);
			if (dic == null)
				return PdfSignatureAppearance.NOT_CERTIFIED;
			var arr = dic.GetAsArray(PdfName.REFERENCE);
			if (arr == null || arr.Size == 0)
				return PdfSignatureAppearance.NOT_CERTIFIED;
			dic = arr.GetAsDict(0);
			if (dic == null)
				return PdfSignatureAppearance.NOT_CERTIFIED;
			dic = dic.GetAsDict(PdfName.TRANSFORMPARAMS);
			if (dic == null)
				return PdfSignatureAppearance.NOT_CERTIFIED;
			var p = dic.GetAsNumber(PdfName.P);
			if (p == null)
				return PdfSignatureAppearance.NOT_CERTIFIED;
			return p.IntValue;
		}

		/**
		* Checks if the document was opened with the owner password so that the end application
		* can decide what level of access restrictions to apply. If the document is not encrypted
		* it will return <CODE>true</CODE>.
		* @return <CODE>true</CODE> if the document was opened with the owner password or if it's not encrypted,
		* <CODE>false</CODE> if the document was opened with the user password
		*/
		public bool IsOpenedWithFullPermissions {
			get {
				return !encrypted || ownerPasswordUsed || unethicalreading;
			}
		}

		virtual public int GetCryptoMode() {
			if (decrypt == null)
				return -1;
			else
				return decrypt.GetCryptoMode();
		}

		virtual public bool IsMetadataEncrypted() {
			if (decrypt == null)
				return false;
			else
				return decrypt.IsMetadataEncrypted();
		}

		/**
		 * Computes user password if standard encryption handler is used with Standard40, Standard128 or AES128 encryption algorithm.
		 *
		 * @return user password, or null if not a standard encryption handler was used,
		 *         if standard encryption handler was used with AES256 encryption algorithm,
		 *         or if ownerPasswordUsed wasn't use to open the document.
		 */
		virtual public byte[] ComputeUserPassword() {
			if (!encrypted || !ownerPasswordUsed) return null;
			return decrypt.ComputeUserPassword(password);
		}

		virtual public void Dispose() {
			Close();
		}
	}
}
