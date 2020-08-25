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

namespace iTextSharp.testutils {

	using System;
	using System.Collections.Generic;
	using System.Diagnostics;
	using System.IO;
	using System.Text;
	using System.util;
	using System.Xml;
	using iTextSharp.text;
	using iTextSharp.text.pdf;
	using System.util.collections;
	using iTextSharp.text.io;
	using iTextSharp.text.pdf.parser;
	using Path = System.IO.Path;

	public class CompareTool {

		protected class ObjectPath {

			protected RefKey baseCmpObject;
			protected RefKey baseOutObject;
			protected Stack<PathItem> path = new Stack<PathItem>();
			protected Stack<Pair<RefKey>> indirects = new Stack<Pair<RefKey>>();

			public ObjectPath() {
			}

			public ObjectPath(RefKey baseCmpObject, RefKey baseOutObject) {
				this.baseCmpObject = baseCmpObject;
				this.baseOutObject = baseOutObject;
			}

			private ObjectPath(RefKey baseCmpObject, RefKey baseOutObject, Stack<PathItem> path) {
				this.baseCmpObject = baseCmpObject;
				this.baseOutObject = baseOutObject;
				this.path = path;
			}

			protected class Pair<T> {

				private readonly T first;
				private readonly T second;

				public Pair(T first, T second) {
					this.first = first;
					this.second = second;
				}

				public override bool Equals(object obj) {
					return (obj is Pair<T> && this.first.Equals(((Pair<T>)obj).first) &&
							this.second.Equals(((Pair<T>)obj).second));
				}

				public override int GetHashCode() {
					return this.first.GetHashCode() * 31 + this.second.GetHashCode();
				}
			}

			protected abstract class PathItem {
				public abstract XmlNode ToXmlNode(XmlDocument document);
			}

			private class DictPathItem : PathItem {

				readonly string key;

				public DictPathItem(string key) {
					this.key = key;
				}

				public override string ToString() {
					return "Dict key: " + this.key;
				}

				public override int GetHashCode() {
					return this.key.GetHashCode();
				}

				public override bool Equals(object obj) {
					return obj is DictPathItem && this.key.Equals(((DictPathItem)obj).key);
				}

				public override XmlNode ToXmlNode(XmlDocument document) {
					XmlNode element = document.CreateElement("dictKey");
					element.AppendChild(document.CreateTextNode(this.key));
					return element;
				}
			}

			private class ArrayPathItem : PathItem {

				readonly int index;

				public ArrayPathItem(int index) {
					this.index = index;
				}

				public override string ToString() {
					return "Array index: " + this.index.ToString();
				}

				public override int GetHashCode() {
					return this.index;
				}

				public override bool Equals(object obj) {
					return obj is ArrayPathItem && this.index == ((ArrayPathItem)obj).index;
				}

				public override XmlNode ToXmlNode(XmlDocument document) {
					XmlNode element = document.CreateElement("arrayIndex");
					element.AppendChild(document.CreateTextNode(this.index.ToString()));
					return element;
				}
			}

			private class OffsetPathItem : PathItem {

				private readonly int offset;

				public OffsetPathItem(int offset) {
					this.offset = offset;
				}

				public override string ToString() {
					return "Offset: " + this.offset;
				}

				public override int GetHashCode() {
					return this.offset;
				}

				public override bool Equals(object obj) {
					return obj is OffsetPathItem && this.offset == ((OffsetPathItem)obj).offset;
				}

				public override XmlNode ToXmlNode(XmlDocument document) {
					XmlNode element = document.CreateElement("offset");
					element.AppendChild(document.CreateTextNode(this.offset.ToString()));

					return element;
				}
			}

			public ObjectPath ResetDirectPath(RefKey baseCmpObject, RefKey baseOutObject) {
				var newPath = new ObjectPath(baseCmpObject, baseOutObject);
				newPath.indirects = new Stack<Pair<RefKey>>(new Stack<Pair<RefKey>>(this.indirects));
				newPath.indirects.Push(new Pair<RefKey>(baseCmpObject, baseOutObject));
				return newPath;
			}

			public bool IsComparing(RefKey baseCmpObject, RefKey baseOutObject) {
				return this.indirects.Contains(new Pair<RefKey>(baseCmpObject, baseOutObject));
			}

			public void PushArrayItemToPath(int index) {
				this.path.Push(new ArrayPathItem(index));
			}

			public void PushDictItemToPath(string key) {
				this.path.Push(new DictPathItem(key));
			}

			public void PushOffsetToPath(int offset) {
				this.path.Push(new OffsetPathItem(offset));
			}

			public void Pop() {
				this.path.Pop();
			}

			public override string ToString() {
				var sb = new StringBuilder();

				foreach (var pathItem in this.path) {
					sb.Insert(0, "\n" + pathItem.ToString());
				}

				sb.Insert(0, string.Format("Base cmp object: {0} obj. Base out object: {1} obj", this.baseCmpObject, this.baseOutObject));

				return sb.ToString();
			}

			public override int GetHashCode() {
				var code1 = this.baseCmpObject != null ? this.baseCmpObject.GetHashCode() : 1;
				var code2 = this.baseOutObject != null ? this.baseOutObject.GetHashCode() : 1;
				var hashCode = code1 * 31 + code2;

				foreach (var pathItem in this.path) {
					hashCode *= 31;
					hashCode += pathItem.GetHashCode();
				}

				return hashCode;
			}

			public override bool Equals(object obj) {
				return obj is ObjectPath && this.baseCmpObject.Equals(((ObjectPath)obj).baseCmpObject) && this.baseOutObject.Equals(((ObjectPath)obj).baseOutObject) &&
						Util.AreEqual(this.path, ((ObjectPath)obj).path);
			}

			public object Clone() {
				return new ObjectPath(this.baseCmpObject, this.baseOutObject, new Stack<PathItem>(new Stack<PathItem>(this.path)));
			}

			public XmlNode ToXmlNode(XmlDocument document) {
				var baseNode = document.CreateElement("base");
				baseNode.SetAttribute("cmp", this.baseCmpObject.ToString() + " obj");
				baseNode.SetAttribute("out", this.baseOutObject.ToString() + " obj");

				var element = document.CreateElement("path");

				foreach (var pathItem in this.path) {
					element.PrependChild(pathItem.ToXmlNode(document));
				}

				element.PrependChild(baseNode);

				return element;
			}
		}

		protected class CompareResult {

			protected Dictionary<ObjectPath, string> differences = new Dictionary<ObjectPath, string>();
			protected int messageLimit = 1;

			public CompareResult(int messageLimit) {
				this.messageLimit = messageLimit;
			}

#pragma warning disable IDE1006 // Naming Styles
			public bool isOk() {
#pragma warning restore IDE1006 // Naming Styles
				return this.differences.Count == 0;
			}

			public int GetErrorCount() {
				return this.differences.Count;
			}

			public bool IsMessageLimitReached() {
				return this.differences.Count >= this.messageLimit;
			}

			public string GetReport() {
				var sb = new StringBuilder();
				var firstEntry = true;

				foreach (var entry in this.differences) {
					if (!firstEntry) {
						sb.Append("-----------------------------").Append("\n");
					}

					var diffPath = entry.Key;
					sb.Append(entry.Value).Append("\n").Append(diffPath.ToString()).Append("\n");
					firstEntry = false;
				}

				return sb.ToString();
			}

			public void AddError(ObjectPath path, string message) {
				if (this.differences.Count < this.messageLimit && !this.differences.ContainsKey(path)) {
					this.differences[((ObjectPath)path.Clone())] = message;
				}
			}

			public void WriteReportToXml(Stream stream) {
				var xmlReport = new XmlDocument();
				var errors = xmlReport.CreateElement("errors");
				errors.SetAttribute("count", this.differences.Count.ToString());

				var root = xmlReport.CreateElement("report");
				root.AppendChild(errors);

				foreach (var entry in this.differences) {
					XmlNode errorNode = xmlReport.CreateElement("error");
					XmlNode message = xmlReport.CreateElement("message");
					message.AppendChild(xmlReport.CreateTextNode(entry.Value));
					var path = entry.Key.ToXmlNode(xmlReport);
					errorNode.AppendChild(message);
					errorNode.AppendChild(path);
					errors.AppendChild(errorNode);
				}

				xmlReport.AppendChild(root);
				xmlReport.PreserveWhitespace = true;

				using (var writer = new XmlTextWriter(stream, null)) {
					writer.Formatting = Formatting.Indented;
					xmlReport.Save(writer);
				}
			}
		}

		private readonly string gsExec;
		private readonly string compareExec;
		private const string gsParams = " -dNOPAUSE -dBATCH -sDEVICE=png16m -r150 -sOutputFile=<outputfile> <inputfile>";
		private const string compareParams = " \"<image1>\" \"<image2>\" \"<difference>\"";

		private const string cannotOpenTarGetDirectory = "Cannot open tarGet directory for <filename>.";
		private const string gsFailed = "GhostScript failed for <filename>.";
		private const string unexpectedNumberOfPages = "Unexpected number of pages for <filename>.";
		private const string differentPages = "File <filename> differs on page <pagenumber>.";
		private const string undefinedGsPath = "Path to GhostScript is not specified. Please use -DgsExec=<path_to_ghostscript> (e.g. -DgsExec=\"C:/Program Files/gs/gs9.14/bin/gswin32c.exe\")";

		private const string ignoredAreasPrefix = "ignored_areas_";

		private string cmpPdf;
		private string cmpPdfName;
		private string cmpImage;
		private string outPdf;
		private string outPdfName;
		private string outImage;

		private IList<PdfDictionary> outPages;
		private IList<RefKey> outPagesRef;
		private IList<PdfDictionary> cmpPages;
		private IList<RefKey> cmpPagesRef;

		private int compareByContentErrorsLimit = 1;
		private bool generateCompareByContentXmlReport = false;
		private string xmlReportName = "report";
		private double floatComparisonError = 0;
		// if false, the error will be relative
		private bool absoluteError = true;

		public CompareTool() {
			this.gsExec = Environment.GetEnvironmentVariable("gsExec");
			this.compareExec = Environment.GetEnvironmentVariable("compareExec");
		}

		private string Compare(string outPath, string differenceImagePrefix, IDictionary<int, IList<Rectangle>> ignoredAreas) {
			return this.Compare(outPath, differenceImagePrefix, ignoredAreas, null);
		}

		private string Compare(string outPath, string differenceImagePrefix, IDictionary<int, IList<Rectangle>> ignoredAreas, IList<int> equalPages) {
			if (this.gsExec == null) {
				return undefinedGsPath;
			}

			if (!File.Exists(this.gsExec)) {
				return this.gsExec + " does not exist";
			}

			try {
				DirectoryInfo tarGetDir;
				FileSystemInfo[] allImageFiles;
				FileSystemInfo[] imageFiles;
				FileSystemInfo[] cmpImageFiles;
				if (Directory.Exists(outPath)) {
					tarGetDir = new DirectoryInfo(outPath);
					allImageFiles = tarGetDir.GetFileSystemInfos("*.png");
					imageFiles = Array.FindAll(allImageFiles, this.PngPredicate);
					foreach (var fileSystemInfo in imageFiles) {
						fileSystemInfo.Delete();
					}

					cmpImageFiles = Array.FindAll(allImageFiles, this.CmpPngPredicate);
					foreach (var fileSystemInfo in cmpImageFiles) {
						fileSystemInfo.Delete();
					}
				} else {
					tarGetDir = Directory.CreateDirectory(outPath);
				}

				if (File.Exists(outPath + differenceImagePrefix)) {
					File.Delete(outPath + differenceImagePrefix);
				}

				if (ignoredAreas != null && ignoredAreas.Count > 0) {
					var cmpReader = new PdfReader(this.cmpPdf);
					var outReader = new PdfReader(this.outPdf);
					var outStamper = new PdfStamper(outReader,
						new FileStream(outPath + ignoredAreasPrefix + this.outPdfName, FileMode.Create));
					var cmpStamper = new PdfStamper(cmpReader,
						new FileStream(outPath + ignoredAreasPrefix + this.cmpPdfName, FileMode.Create));

					foreach (var entry in ignoredAreas) {
						var pageNumber = entry.Key;
						var rectangles = entry.Value;

						if (rectangles != null && rectangles.Count > 0) {
							var outCB = outStamper.GetOverContent(pageNumber);
							var cmpCB = cmpStamper.GetOverContent(pageNumber);

							foreach (var rect in rectangles) {
								rect.BackgroundColor = BaseColor.BLACK;
								outCB.Rectangle(rect);
								cmpCB.Rectangle(rect);
							}
						}
					}

					outStamper.Close();
					cmpStamper.Close();

					outReader.Close();
					cmpReader.Close();

					this.Init(outPath + ignoredAreasPrefix + this.outPdfName, outPath + ignoredAreasPrefix + this.cmpPdfName);
				}

				var gsParams = CompareTool.gsParams.Replace("<outputfile>", outPath + this.cmpImage).Replace("<inputfile>", this.cmpPdf);
				var p = new Process();
				p.StartInfo.FileName = this.@gsExec;
				p.StartInfo.Arguments = @gsParams;
				p.StartInfo.UseShellExecute = false;
				p.StartInfo.RedirectStandardError = true;
				p.StartInfo.RedirectStandardOutput = true;
				p.StartInfo.CreateNoWindow = true;
				p.Start();

				string line;
				while ((line = p.StandardOutput.ReadLine()) != null) {
					Console.Out.WriteLine(line);
				}
				p.StandardOutput.Close(); ;
				while ((line = p.StandardError.ReadLine()) != null) {
					Console.Out.WriteLine(line);
				}
				p.StandardError.Close();
				p.WaitForExit();
				if (p.ExitCode == 0) {
					gsParams = CompareTool.gsParams.Replace("<outputfile>", outPath + this.outImage).Replace("<inputfile>", this.outPdf);
					p = new Process();
					p.StartInfo.FileName = this.@gsExec;
					p.StartInfo.Arguments = @gsParams;
					p.StartInfo.UseShellExecute = false;
					p.StartInfo.RedirectStandardError = true;
					p.StartInfo.RedirectStandardOutput = true;
					p.StartInfo.CreateNoWindow = true;
					p.Start();
					while ((line = p.StandardOutput.ReadLine()) != null) {
						Console.Out.WriteLine(line);
					}
					p.StandardOutput.Close(); ;
					while ((line = p.StandardError.ReadLine()) != null) {
						Console.Out.WriteLine(line);
					}
					p.StandardError.Close();
					p.WaitForExit();

					if (p.ExitCode == 0) {
						allImageFiles = tarGetDir.GetFileSystemInfos("*.png");
						imageFiles = Array.FindAll(allImageFiles, this.PngPredicate);
						cmpImageFiles = Array.FindAll(allImageFiles, this.CmpPngPredicate);
						var bUnexpectedNumberOfPages = imageFiles.Length != cmpImageFiles.Length;
						var cnt = Math.Min(imageFiles.Length, cmpImageFiles.Length);
						if (cnt < 1) {
							return "No files for comparing!!!\nThe result or sample pdf file is not processed by GhostScript.";
						}
						Array.Sort(imageFiles, new ImageNameComparator());
						Array.Sort(cmpImageFiles, new ImageNameComparator());
						string differentPagesFail = null;
						for (var i = 0; i < cnt; i++) {
							if (equalPages != null && equalPages.Contains(i)) {
								continue;
							}

							Console.Out.WriteLine("Comparing page " + (i + 1).ToString() + " (" + imageFiles[i].FullName + ")...");
							var is1 = new FileStream(imageFiles[i].FullName, FileMode.Open);
							var is2 = new FileStream(cmpImageFiles[i].FullName, FileMode.Open);
							var cmpResult = this.CompareStreams(is1, is2);
							is1.Close();
							is2.Close();
							if (!cmpResult) {
								if (File.Exists(this.compareExec)) {
									var compareParams = CompareTool.compareParams.Replace("<image1>", imageFiles[i].FullName).Replace("<image2>", cmpImageFiles[i].FullName).Replace("<difference>", outPath + differenceImagePrefix + (i + 1).ToString() + ".png");
									p = new Process();
									p.StartInfo.FileName = this.@compareExec;
									p.StartInfo.Arguments = @compareParams;
									p.StartInfo.UseShellExecute = false;
									p.StartInfo.RedirectStandardError = true;
									p.StartInfo.CreateNoWindow = true;
									p.Start();

									while ((line = p.StandardError.ReadLine()) != null) {
										Console.Out.WriteLine(line);
									}
									p.StandardError.Close();
									p.WaitForExit();
									if (p.ExitCode == 0) {
										if (differentPagesFail == null) {
											differentPagesFail =
												differentPages.Replace("<filename>", this.outPdf).Replace("<pagenumber>",
																									 (i + 1).ToString());
											differentPagesFail += "\nPlease, examine " + outPath + differenceImagePrefix + (i + 1).ToString() +
																  ".png for more details.";
										} else {
											differentPagesFail =
												"File " + this.outPdf + " differs.\nPlease, examine difference images for more details.";
										}
									} else {
										differentPagesFail = differentPages.Replace("<filename>", this.outPdf).Replace("<pagenumber>", (i + 1).ToString());
										Console.Out.WriteLine("Invalid compareExec variable.");
									}
								} else {
									differentPagesFail =
												differentPages.Replace("<filename>", this.outPdf).Replace("<pagenumber>",
																									 (i + 1).ToString());
									differentPagesFail += "\nYou can optionally specify path to ImageMagick compare tool (e.g. -DcompareExec=\"C:/Program Files/ImageMagick-6.5.4-2/compare.exe\") to visualize differences.";
									break;
								}
							} else {
								Console.Out.WriteLine("done.");
							}
						}
						if (differentPagesFail != null) {
							return differentPagesFail;
						} else {
							if (bUnexpectedNumberOfPages) {
								return unexpectedNumberOfPages.Replace("<filename>", this.outPdf);
							}
						}
					} else {
						return gsFailed.Replace("<filename>", this.outPdf);
					}
				} else {
					return gsFailed.Replace("<filename>", this.cmpPdf);
				}
			} catch (Exception) {
				return cannotOpenTarGetDirectory.Replace("<filename>", this.outPdf);
			}

			return null;
		}

		virtual public string Compare(string outPdf, string cmpPdf, string outPath, string differenceImagePrefix, IDictionary<int, IList<Rectangle>> ignoredAreas) {
			this.Init(outPdf, cmpPdf);
			return this.Compare(outPath, differenceImagePrefix, ignoredAreas);
		}

		virtual public string Compare(string outPdf, string cmpPdf, string outPath, string differenceImagePrefix) {
			this.Init(outPdf, cmpPdf);
			return this.Compare(outPath, differenceImagePrefix, null);
		}

		/**
		 * Sets the maximum errors count which will be returned as the result of the comparison.
		 * @param compareByContentMaxErrorCount the errors count.
		 * @return Returns this.
		 */
		public virtual CompareTool SetCompareByContentErrorsLimit(int compareByContentMaxErrorCount) {
			this.compareByContentErrorsLimit = compareByContentMaxErrorCount;
			return this;
		}

		public virtual void SetGenerateCompareByContentXmlReport(bool generateCompareByContentXmlReport) {
			this.generateCompareByContentXmlReport = generateCompareByContentXmlReport;
		}

		/**
		 * Sets the absolute error parameter which will be used in floating point numbers comparison.
		 * @param error the epsilon new value.
		 * @return Returns this.
		 */
		public CompareTool SetFloatAbsoluteError(float error) {
			this.floatComparisonError = error;
			this.absoluteError = true;
			return this;
		}

		/**
		 * Sets the relative error parameter which will be used in floating point numbers comparison.
		 * @param error the epsilon new value.
		 * @return Returns this.
		 */
		public CompareTool SetFloatRelativeError(float error) {
			this.floatComparisonError = error;
			this.absoluteError = false;
			return this;
		}

		public void SetXmlReportName(string reportName) {
			this.xmlReportName = reportName;
		}

		public string GetXmlReportName() {
			return this.xmlReportName;
		}

		private string CompareByContent(string outPath, string differenceImagePrefix, IDictionary<int, IList<Rectangle>> ignoredAreas) {
			Console.Write("[itext] INFO  Comparing by content..........");

			var compareResult = new CompareResult(this.compareByContentErrorsLimit);
			IList<int> equalPages = new List<int>(this.cmpPages.Count);
			using (var outReader = new PdfReader(this.outPdf)) {

				this.outPages = new List<PdfDictionary>();
				this.outPagesRef = new List<RefKey>();
				this.LoadPagesFromReader(outReader, this.outPages, this.outPagesRef);

				using (var cmpReader = new PdfReader(this.cmpPdf)) {

					this.cmpPages = new List<PdfDictionary>();
					this.cmpPagesRef = new List<RefKey>();
					this.LoadPagesFromReader(cmpReader, this.cmpPages, this.cmpPagesRef);

					if (this.outPages.Count != this.cmpPages.Count) {
						return this.Compare(outPath, differenceImagePrefix, ignoredAreas);
					}

					for (var i = 0; i < this.cmpPages.Count; i++) {
						var currentPath = new ObjectPath(this.cmpPagesRef[i], this.outPagesRef[i]);
						if (this.CompareDictionariesExtended(this.outPages[i], this.cmpPages[i], currentPath, compareResult)) {
							equalPages.Add(i);
						}
					}

					var outStructTree = outReader.Catalog.Get(PdfName.STRUCTTREEROOT);
					var cmpStructTree = cmpReader.Catalog.Get(PdfName.STRUCTTREEROOT);
					var outStructTreeRef = outStructTree == null ? null : new RefKey((PdfIndirectReference)outStructTree);
					var cmpStructTreeRef = cmpStructTree == null ? null : new RefKey((PdfIndirectReference)cmpStructTree);
					this.CompareObjects(outStructTree, cmpStructTree, new ObjectPath(outStructTreeRef, cmpStructTreeRef), compareResult);

					var outOcProperties = outReader.Catalog.Get(PdfName.OCPROPERTIES);
					var cmpOcProperties = cmpReader.Catalog.Get(PdfName.OCPROPERTIES);
					var outOcPropertiesRef = outOcProperties is PdfIndirectReference ? new RefKey((PdfIndirectReference)outOcProperties) : null;
					var cmpOcPropertiesRef = cmpOcProperties is PdfIndirectReference ? new RefKey((PdfIndirectReference)cmpOcProperties) : null;
					this.CompareObjects(outOcProperties, cmpOcProperties, new ObjectPath(outOcPropertiesRef, cmpOcPropertiesRef), compareResult);

				}
			}

			if (this.generateCompareByContentXmlReport) {
				try {
					compareResult.WriteReportToXml(new FileStream(outPath + "/" + this.xmlReportName + ".xml", FileMode.Create));
				} catch (Exception) { }
			}


			if (equalPages.Count == this.cmpPages.Count && compareResult.isOk()) {
				Console.WriteLine("OK");
				Console.Out.Flush();
				return null;
			} else {
				Console.WriteLine("Fail");
				Console.Out.Flush();
				var compareByContentReport = "Compare by content report:\n" + compareResult.GetReport();
				Console.WriteLine(compareByContentReport);
				Console.Out.Flush();
				var message = this.Compare(outPath, differenceImagePrefix, ignoredAreas, equalPages);
				if (message == null || message.Length == 0) {
					return "Compare by content fails. No visual differences";
				}

				return message;
			}
		}

		public virtual string CompareByContent(string outPdf, string cmpPdf, string outPath, string differenceImagePrefix, IDictionary<int, IList<Rectangle>> ignoredAreas) {
			this.Init(outPdf, cmpPdf);
			return this.CompareByContent(outPath, differenceImagePrefix, ignoredAreas);
		}

		public virtual string CompareByContent(string outPdf, string cmpPdf, string outPath, string differenceImagePrefix) {
			return this.CompareByContent(outPdf, cmpPdf, outPath, differenceImagePrefix, null);
		}

		private void LoadPagesFromReader(PdfReader reader, IList<PdfDictionary> pages, IList<RefKey> pagesRef) {
			var pagesDict = reader.Catalog.Get(PdfName.PAGES);
			this.AddPagesFromDict(pagesDict, pages, pagesRef);
		}

		private void AddPagesFromDict(PdfObject dictRef, IList<PdfDictionary> pages, IList<RefKey> pagesRef) {
			var dict = (PdfDictionary)PdfReader.GetPdfObject(dictRef);
			if (dict.IsPages()) {
				var kids = dict.GetAsArray(PdfName.KIDS);
				if (kids == null) {
					return;
				}

				foreach (var kid in kids) {
					this.AddPagesFromDict(kid, pages, pagesRef);
				}
			} else if (dict.IsPage()) {
				pages.Add(dict);
				pagesRef.Add(new RefKey((PRIndirectReference)dictRef));
			}
		}

		private bool CompareObjects(PdfObject outObj, PdfObject cmpObj, ObjectPath currentPath, CompareResult compareResult) {
			var outDirectObj = PdfReader.GetPdfObject(outObj);
			var cmpDirectObj = PdfReader.GetPdfObject(cmpObj);

			if (cmpDirectObj == null && outDirectObj == null) {
				return true;
			}

			if (outDirectObj == null) {
				compareResult.AddError(currentPath, "Expected object was not found.");
				return false;
			} else if (cmpDirectObj == null) {
				compareResult.AddError(currentPath, "Found object which was not expected to be found.");
				return false;
			} else if (cmpDirectObj.Type != outDirectObj.Type) {
				compareResult.AddError(currentPath, string.Format("Types do not match. Expected: {0}. Found: {1}.", cmpDirectObj.GetType().Name, outDirectObj.GetType().Name));
				return false;
			}

			if (cmpObj.IsIndirect() && outObj.IsIndirect()) {
				if (currentPath.IsComparing(new RefKey((PdfIndirectReference)cmpObj), new RefKey((PdfIndirectReference)outObj))) {
					return true;
				}

				currentPath = currentPath.ResetDirectPath(new RefKey((PdfIndirectReference)cmpObj), new RefKey((PdfIndirectReference)outObj));
			}

			if (cmpDirectObj.IsDictionary() && ((PdfDictionary)cmpDirectObj).IsPage()) {
				if (!outDirectObj.IsDictionary() || !((PdfDictionary)outDirectObj).IsPage()) {
					if (compareResult != null && currentPath != null) {
						compareResult.AddError(currentPath, "Expected a page. Found not a page.");
					}

					return false;
				}
				var cmpRefKey = new RefKey((PRIndirectReference)cmpObj);
				var outRefKey = new RefKey((PRIndirectReference)outObj);
				// References to the same page
				if (this.cmpPagesRef.Contains(cmpRefKey) && this.cmpPagesRef.IndexOf(cmpRefKey) == this.outPagesRef.IndexOf(outRefKey)) {
					return true;
				}

				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, string.Format("The dictionaries refer to different pages. Expected page number: {0}. Found: {1}",
							this.cmpPagesRef.IndexOf(cmpRefKey), this.outPagesRef.IndexOf(outRefKey)));
				}

				return false;
			}

			if (cmpDirectObj.IsDictionary()) {
				if (!this.CompareDictionariesExtended((PdfDictionary)outDirectObj, (PdfDictionary)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj.IsStream()) {
				if (!this.CompareStreamsExtended((PRStream)outDirectObj, (PRStream)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj.IsArray()) {
				if (!this.CompareArraysExtended((PdfArray)outDirectObj, (PdfArray)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj.IsName()) {
				if (!this.CompareNamesExtended((PdfName)outDirectObj, (PdfName)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj.IsNumber()) {
				if (!this.CompareNumbersExtended((PdfNumber)outDirectObj, (PdfNumber)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj.IsString()) {
				if (!this.CompareStringsExtended((PdfString)outDirectObj, (PdfString)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj.IsBoolean()) {
				if (!this.CompareBooleansExtended((PdfBoolean)outDirectObj, (PdfBoolean)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (cmpDirectObj is PdfLiteral) {
				if (!this.CompareLiteralsExtended((PdfLiteral)outDirectObj, (PdfLiteral)cmpDirectObj, currentPath, compareResult)) {
					return false;
				}
			} else if (outDirectObj.IsNull() && cmpDirectObj.IsNull()) {
			} else {
				throw new InvalidOperationException();
			}
			return true;
		}

		public bool CompareDictionaries(PdfDictionary outDict, PdfDictionary cmpDict) {
			return this.CompareDictionariesExtended(outDict, cmpDict, null, null);
		}

		private bool CompareDictionariesExtended(PdfDictionary outDict, PdfDictionary cmpDict, ObjectPath currentPath, CompareResult compareResult) {
			if (cmpDict != null && outDict == null || outDict != null && cmpDict == null) {
				compareResult.AddError(currentPath, "One of the dictionaries is null, the other is not.");
				return false;
			}

			var dictsAreSame = true;
			// Iterate through the union of the keys of the cmp and out dictionaries!
			var mergedKeys = new HashSet2<PdfName>(cmpDict.Keys);
			mergedKeys.AddAll(outDict.Keys);

			foreach (var key in mergedKeys) {
				if (key.CompareTo(PdfName.PARENT) == 0 || key.CompareTo(PdfName.P) == 0) {
					continue;
				}

				if (outDict.IsStream() && cmpDict.IsStream() && (key.Equals(PdfName.FILTER) || key.Equals(PdfName.LENGTH))) {
					continue;
				}

				if (key.CompareTo(PdfName.BASEFONT) == 0 || key.CompareTo(PdfName.FONTNAME) == 0) {
					var cmpObj = cmpDict.GetDirectObject(key);
					if (cmpObj.IsName() && cmpObj.ToString().IndexOf('+') > 0) {
						var outObj = outDict.GetDirectObject(key);
						if (!outObj.IsName() || outObj.ToString().IndexOf('+') == -1) {
							if (compareResult != null && currentPath != null) {
								compareResult.AddError(currentPath, string.Format("PdfDictionary {0} entry: Expected: {1}. Found: {2}", key.ToString(), cmpObj.ToString(), outObj.ToString()));
							}

							dictsAreSame = false;
						} else {
							var cmpName = cmpObj.ToString().Substring(cmpObj.ToString().IndexOf('+'));
							var outName = outObj.ToString().Substring(outObj.ToString().IndexOf('+'));
							if (!cmpName.Equals(outName)) {
								if (compareResult != null && currentPath != null) {
									compareResult.AddError(currentPath, string.Format("PdfDictionary {0} entry: Expected: {1}. Found: {2}", key.ToString(), cmpObj.ToString(), outObj.ToString()));
								}

								dictsAreSame = false;
							}
						}
						continue;
					}
				}

				if (this.floatComparisonError != 0 && cmpDict.IsPage() && outDict.IsPage() && key.Equals(PdfName.CONTENTS)) {
					if (!this.CompareContentStreamsByParsingExtended(outDict.GetDirectObject(key), cmpDict.GetDirectObject(key),
							(PdfDictionary)outDict.GetDirectObject(PdfName.RESOURCES), (PdfDictionary)cmpDict.GetDirectObject(PdfName.RESOURCES),
							currentPath, compareResult)) {
						dictsAreSame = false;
					}
					continue;
				}

				if (currentPath != null) {
					currentPath.PushDictItemToPath(key.ToString());
				}

				dictsAreSame = this.CompareObjects(outDict.Get(key), cmpDict.Get(key), currentPath, compareResult) && dictsAreSame;
				if (currentPath != null) {
					currentPath.Pop();
				}

				if (!dictsAreSame && (currentPath == null || compareResult == null || compareResult.IsMessageLimitReached())) {
					return false;
				}
			}

			return dictsAreSame;
		}

		public bool CompareContentStreamsByParsing(PdfObject outObj, PdfObject cmpObj) {
			return this.CompareContentStreamsByParsingExtended(outObj, cmpObj, null, null, null, null);
		}

		public bool CompareContentStreamsByParsing(PdfObject outObj, PdfObject cmpObj, PdfDictionary outResources, PdfDictionary cmpResources) {
			return this.CompareContentStreamsByParsingExtended(outObj, cmpObj, outResources, cmpResources, null, null);
		}

		private bool CompareContentStreamsByParsingExtended(PdfObject outObj, PdfObject cmpObj, PdfDictionary outResources, PdfDictionary cmpResources, ObjectPath currentPath, CompareResult compareResult) {
			if (outObj.Type != outObj.Type) {
				compareResult.AddError(currentPath, string.Format(
						"PdfObject. Types are different. Expected: {0}. Found: {1}", cmpObj.Type, outObj.Type));
				return false;
			}

			if (outObj.IsArray()) {
				var outArr = (PdfArray)outObj;
				var cmpArr = (PdfArray)cmpObj;
				if (cmpArr.Size != outArr.Size) {
					compareResult.AddError(currentPath, string.Format("PdfArray. Sizes are different. Expected: {0}. Found: {1}", cmpArr.Size, outArr.Size));
					return false;
				}
				for (var i = 0; i < cmpArr.Size; i++) {
					if (!this.CompareContentStreamsByParsingExtended(outArr.GetPdfObject(i), cmpArr.GetPdfObject(i), outResources, cmpResources, currentPath, compareResult)) {
						return false;
					}
				}
			}

			var cmpTokeniser = new PRTokeniser(new RandomAccessFileOrArray(
					new RandomAccessSourceFactory().CreateSource(ContentByteUtils.GetContentBytesFromContentObject(cmpObj))));
			var outTokeniser = new PRTokeniser(new RandomAccessFileOrArray(
					new RandomAccessSourceFactory().CreateSource(ContentByteUtils.GetContentBytesFromContentObject(outObj))));

			var cmpPs = new PdfContentParser(cmpTokeniser);
			var outPs = new PdfContentParser(outTokeniser);

			var cmpOperands = new List<PdfObject>();
			var outOperands = new List<PdfObject>();

			while (cmpPs.Parse(cmpOperands).Count > 0) {
				outPs.Parse(outOperands);
				if (cmpOperands.Count != outOperands.Count) {
					compareResult.AddError(currentPath, string.Format(
							"PdfObject. Different commands lengths. Expected: {0}. Found: {1}", cmpOperands.Count, outOperands.Count));
					return false;
				}
				if (cmpOperands.Count == 1 && this.CompareLiterals((PdfLiteral)cmpOperands[0], new PdfLiteral("BI")) && this.CompareLiterals((PdfLiteral)outOperands[0], new PdfLiteral("BI"))) {
					var cmpStr = (PRStream)cmpObj;
					var outStr = (PRStream)outObj;
					if (null != outStr.GetDirectObject(PdfName.RESOURCES) && null != cmpStr.GetDirectObject(PdfName.RESOURCES)) {
						outResources = (PdfDictionary)outStr.GetDirectObject(PdfName.RESOURCES);
						cmpResources = (PdfDictionary)cmpStr.GetDirectObject(PdfName.RESOURCES);
					}
					if (!this.CompareInlineImagesExtended(outPs, cmpPs, outResources, cmpResources, currentPath, compareResult)) {
						return false;
					}
					continue;
				}
				for (var i = 0; i < cmpOperands.Count; i++) {
					if (!this.CompareObjects(outOperands[i], cmpOperands[i], currentPath, compareResult)) {
						return false;
					}
				}
			}
			return true;
		}

		private bool CompareInlineImagesExtended(PdfContentParser outPs, PdfContentParser cmpPs, PdfDictionary outDict, PdfDictionary cmpDict, ObjectPath currentPath, CompareResult compareResult) {
			var cmpInfo = InlineImageUtils.ParseInlineImage(cmpPs, cmpDict);
			var outInfo = InlineImageUtils.ParseInlineImage(outPs, outDict);
			return this.CompareObjects(outInfo.ImageDictionary, cmpInfo.ImageDictionary, currentPath, compareResult) &&
				   Util.ArraysAreEqual(outInfo.Samples, cmpInfo.Samples);
		}

		public bool CompareStreams(PRStream outStream, PRStream cmpStream) {
			return this.CompareStreamsExtended(outStream, cmpStream, null, null);
		}

		private bool CompareStreamsExtended(PRStream outStream, PRStream cmpStream, ObjectPath currentPath, CompareResult compareResult) {
			var decodeStreams = PdfName.FLATEDECODE.Equals(outStream.Get(PdfName.FILTER));
			var outStreamBytes = PdfReader.GetStreamBytesRaw(outStream);
			var cmpStreamBytes = PdfReader.GetStreamBytesRaw(cmpStream);
			if (decodeStreams) {
				outStreamBytes = PdfReader.DecodeBytes(outStreamBytes, outStream);
				cmpStreamBytes = PdfReader.DecodeBytes(cmpStreamBytes, cmpStream);
			}
			if (this.floatComparisonError != 0 &&
				PdfName.XOBJECT.Equals(cmpStream.GetDirectObject(PdfName.TYPE)) &&
				PdfName.XOBJECT.Equals(outStream.GetDirectObject(PdfName.TYPE)) &&
				PdfName.FORM.Equals(cmpStream.GetDirectObject(PdfName.SUBTYPE)) &&
				PdfName.FORM.Equals(outStream.GetDirectObject(PdfName.SUBTYPE))) {
				return
					this.CompareContentStreamsByParsingExtended(outStream, cmpStream, outStream.GetAsDict(PdfName.RESOURCES),
						cmpStream.GetAsDict(PdfName.RESOURCES), currentPath, compareResult) &&
					this.CompareDictionariesExtended(outStream, cmpStream, currentPath, compareResult);
			} else {
				if (Util.ArraysAreEqual(outStreamBytes, cmpStreamBytes)) {
					return this.CompareDictionariesExtended(outStream, cmpStream, currentPath, compareResult);
				} else {
					if (cmpStreamBytes.Length != outStreamBytes.Length) {
						if (compareResult != null && currentPath != null) {
							compareResult.AddError(currentPath,
								string.Format("PRStream. Lengths are different. Expected: {0}. Found: {1}",
									cmpStreamBytes.Length, outStreamBytes.Length));
						}
					} else {
						for (var i = 0; i < cmpStreamBytes.Length; i++) {
							if (cmpStreamBytes[i] != outStreamBytes[i]) {
								var l = Math.Max(0, i - 10);
								var r = Math.Min(cmpStreamBytes.Length, i + 10);
								if (compareResult != null && currentPath != null) {
									currentPath.PushOffsetToPath(i);
									compareResult.AddError(currentPath,
										string.Format(
											"PRStream. The bytes differ at index {0}. Expected: {1} ({2}). Found: {3} ({4})",
											i, Encoding.UTF8.GetString(new byte[] { cmpStreamBytes[i] }),
											Encoding.UTF8.GetString(cmpStreamBytes, l, r - l).Replace("\n", "\\n"),
											Encoding.UTF8.GetString(new byte[] { outStreamBytes[i] }),
											Encoding.UTF8.GetString(outStreamBytes, l, r - l).Replace("\n", "\\n")));
									currentPath.Pop();
								}
							}
						}
					}
					return false;
				}
			}
		}

		public bool CompareArrays(PdfArray outArray, PdfArray cmpArray) {
			return this.CompareArraysExtended(outArray, cmpArray, null, null);
		}

		private bool CompareArraysExtended(PdfArray outArray, PdfArray cmpArray, ObjectPath currentPath, CompareResult compareResult) {
			if (outArray == null) {
				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, "Found null. Expected PdfArray.");
				}

				return false;
			} else if (outArray.Size != cmpArray.Size) {
				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, string.Format("PdfArrays. Lengths are different. Expected: {0}. Found: {1}.", cmpArray.Size, outArray.Size));
				}

				return false;
			}
			var arraysAreEqual = true;
			for (var i = 0; i < cmpArray.Size; i++) {
				if (currentPath != null) {
					currentPath.PushArrayItemToPath(i);
				}

				arraysAreEqual = this.CompareObjects(outArray.GetPdfObject(i), cmpArray.GetPdfObject(i), currentPath, compareResult) && arraysAreEqual;
				if (currentPath != null) {
					currentPath.Pop();
				}

				if (!arraysAreEqual && (currentPath == null || compareResult == null || compareResult.IsMessageLimitReached())) {
					return false;
				}
			}

			return arraysAreEqual;
		}

		public bool CompareNames(PdfName outName, PdfName cmpName) {
			return cmpName.CompareTo(outName) == 0;
		}

		private bool CompareNamesExtended(PdfName outName, PdfName cmpName, ObjectPath currentPath, CompareResult compareResult) {
			if (cmpName.CompareTo(outName) == 0) {
				return true;
			} else {
				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, string.Format("PdfName. Expected: {0}. Found: {1}", cmpName.ToString(), outName.ToString()));
				}

				return false;
			}
		}

		public bool CompareNumbers(PdfNumber outNumber, PdfNumber cmpNumber) {
			var difference = Math.Abs(outNumber.DoubleValue - cmpNumber.DoubleValue);
			if (!this.absoluteError && cmpNumber.DoubleValue != 0) {
				difference /= cmpNumber.DoubleValue;
			}
			return difference <= this.floatComparisonError;
		}

		private bool CompareNumbersExtended(PdfNumber outNumber, PdfNumber cmpNumber, ObjectPath currentPath, CompareResult compareResult) {
			if (this.CompareNumbers(outNumber, cmpNumber)) {
				return true;
			} else {
				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, string.Format("PdfNumber. Expected: {0}. Found: {1}", cmpNumber, outNumber));
				}

				return false;
			}
		}

		public bool CompareStrings(PdfString outString, PdfString cmpString) {
			return Util.ArraysAreEqual(cmpString.GetBytes(), outString.GetBytes());
		}

		private bool CompareStringsExtended(PdfString outString, PdfString cmpString, ObjectPath currentPath, CompareResult compareResult) {
			if (Util.ArraysAreEqual(cmpString.GetBytes(), outString.GetBytes())) {
				return true;
			} else {
				var cmpStr = cmpString.ToUnicodeString();
				var outStr = outString.ToUnicodeString();
				if (cmpStr.Length != outStr.Length) {
					if (compareResult != null && currentPath != null) {
						compareResult.AddError(currentPath, string.Format("PdfString. Lengths are different. Expected: {0}. Found: {1}", cmpStr.Length, outStr.Length));
					}
				} else {
					for (var i = 0; i < cmpStr.Length; i++) {
						if (cmpStr[i] != outStr[i]) {
							var l = Math.Max(0, i - 10);
							var r = Math.Min(cmpStr.Length, i + 10);

							if (compareResult != null && currentPath != null) {
								currentPath.PushOffsetToPath(i);
								compareResult.AddError(currentPath, string.Format("PdfString. Characters differ at position {0}. Expected: {1} ({2}). Found: {3} ({4}).",
										i, cmpStr[i], cmpStr.Substring(l, r).Replace("\n", "\\n"),
										outStr[i], outStr.Substring(l, r).Replace("\n", "\\n")));
								currentPath.Pop();
							}

							break;
						}
					}
				}
				return false;
			}
		}

		public bool CompareLiterals(PdfLiteral outLiteral, PdfLiteral cmpLiteral) {
			return Util.ArraysAreEqual(cmpLiteral.GetBytes(), outLiteral.GetBytes());
		}

		private bool CompareLiteralsExtended(PdfLiteral outLiteral, PdfLiteral cmpLiteral, ObjectPath currentPath,
			CompareResult compareResult) {
			if (this.CompareLiterals(outLiteral, cmpLiteral)) {
				return true;
			} else {
				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, string.Format(
						"PdfLiteral. Expected: {0}. Found: {1}", cmpLiteral, outLiteral));
				}

				return false;
			}
		}

		public bool CompareBooleans(PdfBoolean outBoolean, PdfBoolean cmpBoolean) {
			return Util.ArraysAreEqual(cmpBoolean.GetBytes(), outBoolean.GetBytes());
		}

		private bool CompareBooleansExtended(PdfBoolean outBoolean, PdfBoolean cmpBoolean, ObjectPath currentPath, CompareResult compareResult) {
			if (cmpBoolean.BooleanValue == outBoolean.BooleanValue) {
				return true;
			} else {
				if (compareResult != null && currentPath != null) {
					compareResult.AddError(currentPath, string.Format("PdfBoolean. Expected: {0}. Found: {1}.", cmpBoolean.BooleanValue, outBoolean.BooleanValue));
				}

				return false;
			}
		}

		public string CompareXmp(byte[] xmp1, byte[] xmp2) {
			throw new NotImplementedException("Comparing xmls hasn't implemented");
		}

		public string CompareXmp(byte[] xmp1, byte[] xmp2, bool ignoreDateAndProducerProperties) {
			throw new NotImplementedException("Comparing xmls hasn't implemented");
		}

		public string CompareXmp(string outPdf, string cmpPdf) {
			throw new NotImplementedException("Comparing xmls hasn't implemented");
		}

		public string CompareXmp(string outPdf, string cmpPdf, bool ignoreDateAndProducerProperties) {
			throw new NotImplementedException("Comparing xmls hasn't implemented");
		}

		public bool CompareXmls(byte[] xml1, byte[] xml2) {
			throw new NotImplementedException("Comparing xmls in c# hasn't implemented yet. We don't want to make references to external libraries.");
		}

		private void Init(string outPdf, string cmpPdf) {
			this.outPdf = outPdf;
			this.cmpPdf = cmpPdf;
			this.outPdfName = Path.GetFileName(outPdf);
			this.cmpPdfName = Path.GetFileName(cmpPdf);
			//template for GhostScript and ImageMagic
			this.outImage = this.outPdfName + "-%03d.png";
			this.cmpImage = this.cmpPdfName.StartsWith("cmp_") ? this.cmpPdfName + "-%03d.png" : "cmp_" + this.cmpPdfName + "-%03d.png";
		}

		private bool CompareStreams(FileStream is1, FileStream is2) {
			var buffer1 = new byte[64 * 1024];
			var buffer2 = new byte[64 * 1024];
			int len1;
			int len2;
			for (; ; ) {
				len1 = is1.Read(buffer1, 0, 64 * 1024);
				len2 = is2.Read(buffer2, 0, 64 * 1024);
				if (len1 != len2) {
					return false;
				}

				if (len1 == -1 || len1 == 0) {
					break;
				}

				for (var i = 0; i < len1; i++) {
					if (buffer1[i] != buffer2[i]) {
						return false;
					}
				}

				if (len1 < buffer1.Length) {
					break;
				}
			}
			return true;
		}

		public virtual string CompareDocumentInfo(string outPdf, string cmpPdf) {
			Console.Write("[itext] INFO  Comparing document info.......");
			string message = null;
			var outReader = new PdfReader(outPdf);
			var cmpReader = new PdfReader(cmpPdf);
			var cmpInfo = this.ConvertInfo(cmpReader.Info);
			var outInfo = this.ConvertInfo(outReader.Info);
			for (var i = 0; i < cmpInfo.Length; ++i) {
				if (!cmpInfo[i].Equals(outInfo[i])) {
					message = "Document info fail";
					break;
				}
			}
			outReader.Close();
			cmpReader.Close();

			if (message == null) {
				Console.WriteLine("OK");
			} else {
				Console.WriteLine("Fail");
			}

			Console.Out.Flush();

			return message;
		}



		private bool LinksAreSame(PdfAnnotation.PdfImportedLink cmpLink, PdfAnnotation.PdfImportedLink outLink) {
			// Compare link boxes, page numbers the links refer to, and simple parameters (non-indirect, non-arrays, non-dictionaries)

			if (cmpLink.GetDestinationPage() != outLink.GetDestinationPage()) {
				return false;
			}

			if (!cmpLink.GetRect().ToString().Equals(outLink.GetRect().ToString())) {
				return false;
			}

			var cmpParams = cmpLink.GetParameters();
			var outParams = outLink.GetParameters();
			if (cmpParams.Count != outParams.Count) {
				return false;
			}

			foreach (var cmpEntry in cmpParams) {
				var cmpObj = cmpEntry.Value;
				if (!outParams.ContainsKey(cmpEntry.Key)) {
					return false;
				}

				var outObj = outParams[cmpEntry.Key];
				if (cmpObj.Type != outObj.Type) {
					return false;
				}

				switch (cmpObj.Type) {
					case PdfObject.NULL:
					case PdfObject.BOOLEAN:
					case PdfObject.NUMBER:
					case PdfObject.STRING:
					case PdfObject.NAME:
						if (!cmpObj.ToString().Equals(outObj.ToString())) {
							return false;
						}

						break;
				}
			}

			return true;
		}

		virtual public string CompareLinks(string outPdf, string cmpPdf) {
			Console.Write("[itext] INFO  Comparing link annotations....");
			string message = null;
			var outReader = new PdfReader(outPdf);
			var cmpReader = new PdfReader(cmpPdf);
			for (var i = 0; i < outReader.NumberOfPages && i < cmpReader.NumberOfPages; i++) {
				var outLinks = outReader.GetLinks(i + 1);
				var cmpLinks = cmpReader.GetLinks(i + 1);
				if (cmpLinks.Count != outLinks.Count) {
					message = string.Format("Different number of links on page {0}.", i + 1);
					break;
				}
				for (var j = 0; j < cmpLinks.Count; j++) {
					if (!this.LinksAreSame(cmpLinks[j], outLinks[j])) {
						message = string.Format("Different links on page {0}.\n{1}\n{2}", i + 1, cmpLinks[j].ToString(),
							outLinks[j].ToString());
						break;
					}
				}
			}
			outReader.Close();
			cmpReader.Close();
			if (message == null) {
				Console.WriteLine("OK");
			} else {
				Console.WriteLine("Fail");
			}

			Console.Out.Flush();
			return message;
		}

		private string[] ConvertInfo(IDictionary<string, string> info) {
			var convertedInfo = new string[] { "", "", "", "" };
			foreach (var key in info.Keys) {
				if (Util.EqualsIgnoreCase(Meta.TITLE, key)) {
					convertedInfo[0] = info[key];
				} else if (Util.EqualsIgnoreCase(Meta.AUTHOR, key)) {
					convertedInfo[1] = info[key];
				} else if (Util.EqualsIgnoreCase(Meta.SUBJECT, key)) {
					convertedInfo[2] = info[key];
				} else if (Util.EqualsIgnoreCase(Meta.KEYWORDS, key)) {
					convertedInfo[3] = info[key];
				}
			}
			return convertedInfo;
		}

		private bool PngPredicate(FileSystemInfo pathname) {
			var ap = pathname.Name;
			var b1 = ap.EndsWith(".png");
			var b2 = ap.Contains("cmp_");
			return b1 && !b2 && ap.Contains(this.outPdfName);
		}

		private bool CmpPngPredicate(FileSystemInfo pathname) {
			var ap = pathname.Name;
			var b1 = ap.EndsWith(".png");
			var b2 = ap.Contains("cmp_");
			return b1 && b2 && ap.Contains(this.cmpPdfName);
		}

		class ImageNameComparator : IComparer<FileSystemInfo> {
			virtual public int Compare(FileSystemInfo f1, FileSystemInfo f2) {
				var f1Name = f1.FullName;
				var f2Name = f2.FullName;
				return f1Name.CompareTo(f2Name);
			}
		}
	}
}
