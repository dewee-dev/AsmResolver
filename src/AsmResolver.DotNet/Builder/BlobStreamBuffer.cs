using System;
using System.Collections.Generic;
using System.IO;
using AsmResolver.DotNet.Blob;
using AsmResolver.PE.DotNet.Metadata;
using AsmResolver.PE.DotNet.Metadata.Blob;

namespace AsmResolver.DotNet.Builder
{
    /// <summary>
    /// Provides a mutable buffer for building up a blob stream in a .NET portable executable. 
    /// </summary>
    public class BlobStreamBuffer : IMetadataStreamBuffer
    {
        private readonly MemoryStream _rawStream = new MemoryStream();
        private readonly BinaryStreamWriter _writer;
        private readonly IDictionary<byte[], uint> _blobs = new Dictionary<byte[], uint>(ByteArrayEqualityComparer.Instance);

        /// <summary>
        /// Creates a new blob stream buffer with the default blob stream name.
        /// </summary>
        public BlobStreamBuffer()
            : this(BlobStream.DefaultName)
        {
        }

        /// <summary>
        /// Creates a new blob stream buffer.
        /// </summary>
        /// <param name="name">The name of the stream.</param>
        public BlobStreamBuffer(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _writer = new BinaryStreamWriter(_rawStream);
            _writer.WriteByte(0);
        }

        /// <inheritdoc />
        public string Name
        {
            get;
        }

        /// <summary>
        /// Appends raw data to the stream.
        /// </summary>
        /// <param name="data">The data to append.</param>
        /// <returns>The index to the start of the data.</returns>
        /// <remarks>
        /// This method does not index the blob data. Calling <see cref="AppendRawData"/> or <see cref="GetBlobIndex(byte[])"/>
        /// on the same data will append the data a second time.
        /// </remarks>
        public uint AppendRawData(byte[] data)
        {
            uint offset = (uint) _rawStream.Length;
            _writer.WriteBytes(data, 0, data.Length);
            return offset;
        }
        
        /// <summary>
        /// Gets the index to the provided blob. If the blob is not present in the buffer, it will be appended to the end
        /// of the stream.
        /// </summary>
        /// <param name="blob">The blob to lookup or add.</param>
        /// <returns>The index of the blob.</returns>
        public uint GetBlobIndex(byte[] blob)
        {
            if (!_blobs.TryGetValue(blob, out uint offset))
            {
                offset = (uint) _rawStream.Length;
                _writer.WriteCompressedUInt32((uint) blob.Length);
                 AppendRawData(blob);
                _blobs.Add(blob, offset);
            }
            
            return offset;
        }

        /// <summary>
        /// Gets the index to the provided blob signature. If the signature is not present in the buffer, it will be
        /// appended to the end of the stream.
        /// </summary>
        /// <param name="signature">The signature to lookup or add.</param>
        /// <returns>The index of the signature.</returns>
        public uint GetBlobIndex(BlobSignature signature)
        {
            // Serialize blob.
            using var stream = new MemoryStream();
            var writer = new BinaryStreamWriter(stream);
            signature.Write(writer);
            
            return GetBlobIndex(stream.ToArray());
        }

        /// <inheritdoc />
        public IMetadataStream CreateStream()
        {
            return new SerializedBlobStream(Name, _rawStream.ToArray());
        }
    }
}