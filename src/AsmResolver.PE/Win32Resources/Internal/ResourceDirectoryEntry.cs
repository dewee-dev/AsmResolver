// AsmResolver - Executable file format inspection library 
// Copyright (C) 2016-2019 Washi
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 3.0 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA

using System.Text;
using AsmResolver.PE.File;
using AsmResolver.PE.File.Headers;

namespace AsmResolver.PE.Win32Resources.Internal
{
    internal class ResourceDirectoryEntry
    {
        public const int EntrySize = 2 * sizeof(uint);

        private readonly uint _dataOrSubDirOffset;
        
        public ResourceDirectoryEntry(PEFile peFile, IBinaryStreamReader reader, bool isByName)
        {
            IdOrNameOffset = reader.ReadUInt32();
            _dataOrSubDirOffset = reader.ReadUInt32();

            if (isByName)
            {
                uint baseRva = peFile.OptionalHeader
                    .DataDirectories[OptionalHeader.ResourceDirectoryIndex]
                    .VirtualAddress;
                var nameReader = peFile.CreateReaderAtRva(baseRva + IdOrNameOffset);

                int length = nameReader.ReadUInt16();
                var data = new byte[length];
                length = nameReader.ReadBytes(data, 0, length);

                Name = Encoding.Unicode.GetString(data, 0, length);
            }
        }

        public string Name
        {
            get;
        }

        public uint IdOrNameOffset
        {
            get;
        }

        public bool IsByName => Name != null;

        public uint DataOrSubDirOffset => _dataOrSubDirOffset & 0x7FFFFFFF;

        public bool IsData => (_dataOrSubDirOffset & 0x80000000) == 0;
        
        public bool IsSubDirectory => (_dataOrSubDirOffset & 0x80000000) != 0;
    }
}