/*
    This file is part of the iText (R) project.
    Copyright (c) 1998-2019 iText Group NV
    Authors: iText Software.

This program is free software; you can redistribute it and/or modify it under the terms of the GNU Affero General Public License version 3 as published by the Free Software Foundation with the addition of the following permission added to Section 15 as permitted in Section 7(a): FOR ANY PART OF THE COVERED WORK IN WHICH THE COPYRIGHT IS OWNED BY iText Group NV, iText Group NV DISCLAIMS THE WARRANTY OF NON INFRINGEMENT OF THIRD PARTY RIGHTS.

This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU Affero General Public License for more details.
You should have received a copy of the GNU Affero General Public License along with this program; if not, see http://www.gnu.org/licenses or write to the Free Software Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA, 02110-1301 USA, or download the license from the following URL:

http://itextpdf.com/terms-of-use/

The interactive user interfaces in modified source and object code versions of this program must display Appropriate Legal Notices, as required under Section 5 of the GNU Affero General Public License.

In accordance with Section 7(b) of the GNU Affero General Public License, a covered work must retain the producer line in every PDF that is created or manipulated using iText.

You can be released from the requirements of the license by purchasing a commercial license. Buying such a license is mandatory as soon as you develop commercial activities involving the iText software without disclosing the source code of your own applications.
These activities include: offering paid services to customers as an ASP, serving PDFs on the fly in a web application, shipping iText with a closed source product.

For more information, please contact iText Software Corp. at this address: sales@itextpdf.com */
using System;

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Org.BouncyCastle.Crypto.Paddings
{
	/**
	* A wrapper class that allows block ciphers to be used to process data in
	* a piecemeal fashion with padding. The PaddedBufferedBlockCipher
	* outputs a block only when the buffer is full and more data is being added,
	* or on a doFinal (unless the current block in the buffer is a pad block).
	* The default padding mechanism used is the one outlined in Pkcs5/Pkcs7.
	*/
	public class PaddedBufferedBlockCipher
		: BufferedBlockCipher
	{
		private readonly IBlockCipherPadding padding;

		/**
		* Create a buffered block cipher with the desired padding.
		*
		* @param cipher the underlying block cipher this buffering object wraps.
		* @param padding the padding type.
		*/
		public PaddedBufferedBlockCipher(
			IBlockCipher		cipher,
			IBlockCipherPadding	padding)
		{
			this.cipher = cipher;
			this.padding = padding;

			buf = new byte[cipher.GetBlockSize()];
			bufOff = 0;
		}

		/**
		* Create a buffered block cipher Pkcs7 padding
		*
		* @param cipher the underlying block cipher this buffering object wraps.
		*/
		public PaddedBufferedBlockCipher(
			IBlockCipher cipher)
			: this(cipher, new Pkcs7Padding())    { }

		/**
		* initialise the cipher.
		*
		* @param forEncryption if true the cipher is initialised for
		*  encryption, if false for decryption.
		* @param param the key and other data required by the cipher.
		* @exception ArgumentException if the parameters argument is
		* inappropriate.
		*/
		public override void Init(
			bool				forEncryption,
			ICipherParameters	parameters)
		{
			this.forEncryption = forEncryption;

			SecureRandom initRandom = null;
			if (parameters is ParametersWithRandom)
			{
				ParametersWithRandom p = (ParametersWithRandom)parameters;
				initRandom = p.Random;
				parameters = p.Parameters;
			}

			Reset();
			padding.Init(initRandom);
			cipher.Init(forEncryption, parameters);
		}

		/**
		* return the minimum size of the output buffer required for an update
		* plus a doFinal with an input of len bytes.
		*
		* @param len the length of the input.
		* @return the space required to accommodate a call to update and doFinal
		* with len bytes of input.
		*/
		public override int GetOutputSize(
			int length)
		{
			int total = length + bufOff;
			int leftOver = total % buf.Length;

			if (leftOver == 0)
			{
				if (forEncryption)
				{
					return total + buf.Length;
				}

				return total;
			}

			return total - leftOver + buf.Length;
		}

		/**
		* return the size of the output buffer required for an update
		* an input of len bytes.
		*
		* @param len the length of the input.
		* @return the space required to accommodate a call to update
		* with len bytes of input.
		*/
		public override int GetUpdateOutputSize(
			int length)
		{
			int total       = length + bufOff;
			int leftOver    = total % buf.Length;

			if (leftOver == 0)
			{
				return total - buf.Length;
			}

			return total - leftOver;
		}

		/**
		* process a single byte, producing an output block if neccessary.
		*
		* @param in the input byte.
		* @param out the space for any output that might be produced.
		* @param outOff the offset from which the output will be copied.
		* @return the number of output bytes copied to out.
		* @exception DataLengthException if there isn't enough space in out.
		* @exception InvalidOperationException if the cipher isn't initialised.
		*/
		public override int ProcessByte(
			byte	input,
			byte[]	output,
			int		outOff)
		{
			int resultLen = 0;

			if (bufOff == buf.Length)
			{
				resultLen = cipher.ProcessBlock(buf, 0, output, outOff);
				bufOff = 0;
			}

			buf[bufOff++] = input;

			return resultLen;
		}

		/**
		* process an array of bytes, producing output if necessary.
		*
		* @param in the input byte array.
		* @param inOff the offset at which the input data starts.
		* @param len the number of bytes to be copied out of the input array.
		* @param out the space for any output that might be produced.
		* @param outOff the offset from which the output will be copied.
		* @return the number of output bytes copied to out.
		* @exception DataLengthException if there isn't enough space in out.
		* @exception InvalidOperationException if the cipher isn't initialised.
		*/
		public override int ProcessBytes(
			byte[]	input,
			int		inOff,
			int		length,
			byte[]	output,
			int		outOff)
		{
			if (length < 0)
			{
				throw new ArgumentException("Can't have a negative input length!");
			}

			int blockSize = GetBlockSize();
			int outLength = GetUpdateOutputSize(length);

			if (outLength > 0)
			{
				if ((outOff + outLength) > output.Length)
				{
					throw new DataLengthException("output buffer too short");
				}
			}

			int resultLen = 0;
			int gapLen = buf.Length - bufOff;

			if (length > gapLen)
			{
				Array.Copy(input, inOff, buf, bufOff, gapLen);

				resultLen += cipher.ProcessBlock(buf, 0, output, outOff);

				bufOff = 0;
				length -= gapLen;
				inOff += gapLen;

				while (length > buf.Length)
				{
					resultLen += cipher.ProcessBlock(input, inOff, output, outOff + resultLen);

					length -= blockSize;
					inOff += blockSize;
				}
			}

			Array.Copy(input, inOff, buf, bufOff, length);

			bufOff += length;

			return resultLen;
		}

		/**
		* Process the last block in the buffer. If the buffer is currently
		* full and padding needs to be added a call to doFinal will produce
		* 2 * GetBlockSize() bytes.
		*
		* @param out the array the block currently being held is copied into.
		* @param outOff the offset at which the copying starts.
		* @return the number of output bytes copied to out.
		* @exception DataLengthException if there is insufficient space in out for
		* the output or we are decrypting and the input is not block size aligned.
		* @exception InvalidOperationException if the underlying cipher is not
		* initialised.
		* @exception InvalidCipherTextException if padding is expected and not found.
		*/
		public override int DoFinal(
			byte[]  output,
			int     outOff)
		{
			int blockSize = cipher.GetBlockSize();
			int resultLen = 0;

			if (forEncryption)
			{
				if (bufOff == blockSize)
				{
					if ((outOff + 2 * blockSize) > output.Length)
					{
						Reset();

						throw new DataLengthException("output buffer too short");
					}

					resultLen = cipher.ProcessBlock(buf, 0, output, outOff);
					bufOff = 0;
				}

				padding.AddPadding(buf, bufOff);

				resultLen += cipher.ProcessBlock(buf, 0, output, outOff + resultLen);

				Reset();
			}
			else
			{
				if (bufOff == blockSize)
				{
					resultLen = cipher.ProcessBlock(buf, 0, buf, 0);
					bufOff = 0;
				}
				else
				{
					Reset();

					throw new DataLengthException("last block incomplete in decryption");
				}

				try
				{
					resultLen -= padding.PadCount(buf);

					Array.Copy(buf, 0, output, outOff, resultLen);
				}
				finally
				{
					Reset();
				}
			}

			return resultLen;
		}
	}

}
