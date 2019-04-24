//  MeCab -- Yet Another Part-of-Speech and Morphological Analyzer
//
//  Copyright(C) 2001-2006 Taku Kudo <taku@chasen.org>
//  Copyright(C) 2004-2006 Nippon Telegraph and Telephone Corporation
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace NMeCab.Core
{
    public interface IDoubleArray : IDisposable
    {
        int Size { get; }
        int TotalSize { get; }
        unsafe void ExactMatchSearch(byte* key, DoubleArrayResultPair* result, int len, int nodePos);
        unsafe DoubleArrayResultPair ExactMatchSearch(byte* key, int len, int nodePos);
        unsafe int CommonPrefixSearch(byte* key, DoubleArrayResultPair* result, int resultLen, int len, int nodePos = 0);
    }
    public struct DoubleArrayResultPair
    {
        public int Value;

        public int Length;

        public DoubleArrayResultPair(int r, int t)
        {
            this.Value = r;
            this.Length = t;
        }
    }

    /// <summary>
    /// Double-Array Trie の実装
    /// </summary>
    public class DoubleArray : IDoubleArray
    {
        private struct Unit
        {
            public readonly int Base;
            public readonly uint Check;

            public Unit(int b, uint c)
            {
                this.Base = b;
                this.Check = c;
            }
        }

        public const int UnitSize = sizeof(int) + sizeof(uint);

        private Unit[] array;

        public int Size
        {
            get { return this.array.Length; }
        }

        public int TotalSize
        {
            get { return this.Size * UnitSize; }
        }

        public void Open(BinaryReader reader, uint size)
        {
            this.array = new Unit[size / UnitSize];

            for (int i = 0; i < array.Length; i++)
            {
                this.array[i] = new Unit(reader.ReadInt32(), reader.ReadUInt32());
            }
        }

        public unsafe void ExactMatchSearch(byte* key, DoubleArrayResultPair* result, int len, int nodePos)
        {
            *result = this.ExactMatchSearch(key, len, nodePos);
        }

        public unsafe DoubleArrayResultPair ExactMatchSearch(byte* key, int len, int nodePos)
        {
            int b = this.ReadBase(nodePos);
            Unit p;

            for (int i = 0; i < len; i++)
            {
                this.ReadUnit(b + key[i] + 1, out p);
                if (b == p.Check)
                {
                    b = p.Base;
                }
                else
                {
                    return new DoubleArrayResultPair(-1, 0);
                }
            }

            this.ReadUnit(b, out p);
            int n = p.Base;
            if (b == p.Check && n < 0)
            {
                return new DoubleArrayResultPair(-n - 1, len);
            }

            return new DoubleArrayResultPair(-1, 0);
        }

        public unsafe int CommonPrefixSearch(byte* key, DoubleArrayResultPair* result, int resultLen, int len, int nodePos = 0)
        {
            int b = this.ReadBase(nodePos);
            int num = 0;
            int n;
            Unit p;

            for (int i = 0; i < len; i++)
            {
                this.ReadUnit(b, out p);
                n = p.Base;

                if (b == p.Check && n < 0)
                {
                    if (num < resultLen) result[num] = new DoubleArrayResultPair(-n - 1, i);
                    num++;
                }

                this.ReadUnit(b + key[i] + 1, out p);
                if (b == p.Check)
                {
                    b = p.Base;
                }
                else
                {
                    return num;
                }
            }

            this.ReadUnit(b, out p);
            n = p.Base;

            if (b == p.Check && n < 0)
            {
                if (num < resultLen) result[num] = new DoubleArrayResultPair(-n - 1, len);
                num++;
            }

            return num;
        }



        private int ReadBase(int pos)
        {
            return this.array[pos].Base;
        }

        private void ReadUnit(int pos, out Unit unit)
        {
            unit = this.array[pos];
        }

        private bool disposed;

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                
            }

            this.disposed = true;
        }

        ~DoubleArray()
        {
            this.Dispose(false);
        }
    }

    public class DoubleArrayMMF : IDoubleArray
    {
        private struct Unit
        {
            public readonly int Base;
            public readonly uint Check;

            public Unit(int b, uint c)
            {
                this.Base = b;
                this.Check = c;
            }
        }

        public const int UnitSize = sizeof(int) + sizeof(uint);

        private MemoryMappedViewAccessor accessor;

        public int Size
        {
            get { return (int)(this.accessor.Capacity) / UnitSize; }
        }

        public int TotalSize
        {
            get { return (int)(this.accessor.Capacity); }
        }

        public void Open(MemoryMappedFile mmf, long offset, long size)
        {
            this.accessor = mmf.CreateViewAccessor(offset, size, MemoryMappedFileAccess.Read);
        }

        public unsafe void ExactMatchSearch(byte* key, DoubleArrayResultPair* result, int len, int nodePos)
        {
            *result = this.ExactMatchSearch(key, len, nodePos);
        }

        public unsafe DoubleArrayResultPair ExactMatchSearch(byte* key, int len, int nodePos)
        {
            int b = this.ReadBase(nodePos);
            Unit p;

            for (int i = 0; i < len; i++)
            {
                this.ReadUnit(b + key[i] + 1, out p);
                if (b == p.Check)
                {
                    b = p.Base;
                }
                else
                {
                    return new DoubleArrayResultPair(-1, 0);
                }
            }

            this.ReadUnit(b, out p);
            int n = p.Base;
            if (b == p.Check && n < 0)
            {
                return new DoubleArrayResultPair(-n - 1, len);
            }

            return new DoubleArrayResultPair(-1, 0);
        }

        public unsafe int CommonPrefixSearch(byte* key, DoubleArrayResultPair* result, int resultLen, int len, int nodePos = 0)
        {
            int b = this.ReadBase(nodePos);
            int num = 0;
            int n;
            Unit p;

            for (int i = 0; i < len; i++)
            {
                this.ReadUnit(b, out p);
                n = p.Base;

                if (b == p.Check && n < 0)
                {
                    if (num < resultLen) result[num] = new DoubleArrayResultPair(-n - 1, i);
                    num++;
                }

                this.ReadUnit(b + key[i] + 1, out p);
                if (b == p.Check)
                {
                    b = p.Base;
                }
                else
                {
                    return num;
                }
            }

            this.ReadUnit(b, out p);
            n = p.Base;

            if (b == p.Check && n < 0)
            {
                if (num < resultLen) result[num] = new DoubleArrayResultPair(-n - 1, len);
                num++;
            }

            return num;
        }

        private int ReadBase(int pos)
        {
            return this.accessor.ReadInt32(pos * UnitSize);
        }

        private void ReadUnit(int pos, out Unit unit)
        {
            this.accessor.Read<Unit>(pos * UnitSize, out unit);
        }

        private bool disposed;

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
            {
                if (this.accessor != null) this.accessor.Dispose();
            }

            this.disposed = true;
        }

        ~DoubleArrayMMF()
        {
            this.Dispose(false);
        }
    }
}
