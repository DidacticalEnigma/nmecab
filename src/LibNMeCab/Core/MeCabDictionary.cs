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
    public interface IMeCabDictionary : IDisposable
    {
        /// <summary>
        /// 辞書の文字コード
        /// </summary>
        string CharSet { get; }

        /// <summary>
        /// バージョン
        /// </summary>
        uint Version { get; }

        /// <summary>
        /// 辞書のタイプ
        /// </summary>
        DictionaryType Type { get; }

        uint LexSize { get; }

        /// <summary>
        /// 左文脈 ID のサイズ
        /// </summary>
        uint LSize { get; }

        /// <summary>
        /// 右文脈 ID のサイズ
        /// </summary>
        uint RSize { get; }

        /// <summary>
        /// 辞書のファイル名
        /// </summary>
        string FileName { get; }

        void Open(string filePath);
        unsafe DoubleArrayResultPair ExactMatchSearch(string key);
        unsafe DoubleArrayResultPair ExactMatchSearch(char* key, int len, int nodePos = 0);
        unsafe int CommonPrefixSearch(char* key, int len, DoubleArrayResultPair* result, int rLen);
        Token[] GetToken(DoubleArrayResultPair n);
        string GetFeature(uint featurePos);
        bool IsCompatible(IMeCabDictionary d);
    }

    public class MeCabDictionary : IMeCabDictionary
    {
        private const uint DictionaryMagicID = 0xEF718F77u;
        private const uint DicVersion = 102u;

        private Token[] tokens;
        private byte[] features;

        private DoubleArray da = new DoubleArray();

        private Encoding encoding;

        /// <summary>
        /// 辞書の文字コード
        /// </summary>
        public string CharSet
        {
            get { return this.encoding.WebName; }
        }

        /// <summary>
        /// バージョン
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// 辞書のタイプ
        /// </summary>
        public DictionaryType Type { get; private set; }

        public uint LexSize { get; private set; }

        /// <summary>
        /// 左文脈 ID のサイズ
        /// </summary>
        public uint LSize { get; private set; }

        /// <summary>
        /// 右文脈 ID のサイズ
        /// </summary>
        public uint RSize { get; private set; }

        /// <summary>
        /// 辞書のファイル名
        /// </summary>
        public string FileName { get; private set; }

        public void Open(string filePath)
        {
            this.FileName = filePath;
            
            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fileStream))
            {
                this.Open(reader);
            }
        }

        public unsafe void Open(BinaryReader reader)
        {
            uint magic = reader.ReadUInt32();
            //CanSeekの時のみストリーム長のチェック
            if (reader.BaseStream.CanSeek && reader.BaseStream.Length != (magic ^ DictionaryMagicID))
                throw new MeCabInvalidFileException("dictionary file is broken", this.FileName);

            this.Version = reader.ReadUInt32();
            if (this.Version != DicVersion)
                throw new MeCabInvalidFileException("incompatible version", this.FileName);

            this.Type = (DictionaryType)reader.ReadUInt32();
            this.LexSize = reader.ReadUInt32();
            this.LSize = reader.ReadUInt32();
            this.RSize = reader.ReadUInt32();
            uint dSize = reader.ReadUInt32();
            uint tSize = reader.ReadUInt32();
            uint fSize = reader.ReadUInt32();
            reader.ReadUInt32(); //dummy

            string charSet = StrUtils.GetString(reader.ReadBytes(32), Encoding.ASCII);
            this.encoding = StrUtils.GetEncoding(charSet);

            this.da.Open(reader, dSize);

            this.tokens = new Token[tSize / sizeof(Token)];
            for (int i = 0; i < this.tokens.Length; i++)
                this.tokens[i] = Token.Create(reader);

            this.features = reader.ReadBytes((int)fSize);

            if (reader.BaseStream.ReadByte() != -1)
                throw new MeCabInvalidFileException("dictionary file is broken", this.FileName);
        }

        public unsafe DoubleArrayResultPair ExactMatchSearch(string key)
        {
            fixed (char* pKey = key)
                return this.ExactMatchSearch(pKey, key.Length, 0);
        }

        public unsafe DoubleArrayResultPair ExactMatchSearch(char* key, int len, int nodePos = 0)
        {
            //if (this.encoding == Encoding.Unicode)
            //    return this.da.ExactMatchSearch((byte*)key, len, nodePos);

            //エンコード
            int maxByteCount = this.encoding.GetMaxByteCount(len);
            byte* bytes = stackalloc byte[maxByteCount];
            int bytesLen = this.encoding.GetBytes(key, len, bytes, maxByteCount);

            DoubleArrayResultPair result = this.da.ExactMatchSearch(bytes, bytesLen, nodePos);

            //文字数をデコードしたものに変換
            result.Length = this.encoding.GetCharCount(bytes, result.Length);

            return result;
        }

        public unsafe int CommonPrefixSearch(char* key, int len, DoubleArrayResultPair* result, int rLen)
        {
            //if (this.encoding == Encoding.Unicode)
            //    return this.da.CommonPrefixSearch((byte*)key, result, rLen, len);

            //エンコード
            int maxByteLen = this.encoding.GetMaxByteCount(len);
            byte* bytes = stackalloc byte[maxByteLen];
            int bytesLen = this.encoding.GetBytes(key, len, bytes, maxByteLen);

            int n = this.da.CommonPrefixSearch(bytes, result, rLen, bytesLen);

            //文字数をデコードしたものに変換
            for (int i = 0; i < n; i++)
                result[i].Length = this.encoding.GetCharCount(bytes, result[i].Length);

            return n;
        }

        public unsafe Token[] GetToken(DoubleArrayResultPair n)
        {
            Token[] dist = new Token[0xFF & n.Value];
            int tokenPos = n.Value >> 8;
            Array.Copy(this.tokens, tokenPos, dist, 0, dist.Length);
            return dist;
        }

        public string GetFeature(uint featurePos)
        {
            return StrUtils.GetString(this.features, (long)featurePos, this.encoding);
        }

        public bool IsCompatible(IMeCabDictionary d)
        {
            return (this.Version == d.Version &&
                    this.LSize == d.LSize &&
                    this.RSize == d.RSize &&
                    this.CharSet == d.CharSet);
        }

        private bool disposed;

        /// <summary>
        /// 使用されているリソースを開放する
        /// </summary>
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
                if (this.da != null) this.da.Dispose();
            }

            this.disposed = true;
        }

        ~MeCabDictionary()
        {
            this.Dispose(false);
        }
    }

    public class MeCabDictionaryMMF : IMeCabDictionary
    {
        private const uint DictionaryMagicID = 0xEF718F77u;
        private const uint DicVersion = 102u;

        private MemoryMappedFile mmf;
        private MemoryMappedViewAccessor tokens;
        private MemoryMappedViewAccessor features;

        private DoubleArrayMMF da = new DoubleArrayMMF();

        private Encoding encoding;

        /// <summary>
        /// 辞書の文字コード
        /// </summary>
        public string CharSet
        {
            get { return this.encoding.WebName; }
        }

        /// <summary>
        /// バージョン
        /// </summary>
        public uint Version { get; private set; }

        /// <summary>
        /// 辞書のタイプ
        /// </summary>
        public DictionaryType Type { get; private set; }

        public uint LexSize { get; private set; }

        /// <summary>
        /// 左文脈 ID のサイズ
        /// </summary>
        public uint LSize { get; private set; }

        /// <summary>
        /// 右文脈 ID のサイズ
        /// </summary>
        public uint RSize { get; private set; }

        /// <summary>
        /// 辞書のファイル名
        /// </summary>
        public string FileName { get; private set; }

        public void Open(string filePath)
        {
            this.mmf = MemoryMappedFile.CreateFromFile(File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read),
                                                       null, 0L, MemoryMappedFileAccess.Read, HandleInheritability.None, leaveOpen: false);
            this.Open(this.mmf, filePath);
        }

        public void Open(MemoryMappedFile mmf, string filePath = null)
        {
            this.FileName = filePath;

            using (MemoryMappedViewStream stream = mmf.CreateViewStream(
                                                        0L, 0L, MemoryMappedFileAccess.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                uint magic = reader.ReadUInt32();
                if (stream.CanSeek && stream.Length < (magic ^ DictionaryMagicID)) //正確なサイズ取得ができないので不等号で代用
                    throw new MeCabInvalidFileException("dictionary file is broken", filePath);

                this.Version = reader.ReadUInt32();
                if (this.Version != DicVersion)
                    throw new MeCabInvalidFileException("incompatible version", filePath);

                this.Type = (DictionaryType)reader.ReadUInt32();
                this.LexSize = reader.ReadUInt32();
                this.LSize = reader.ReadUInt32();
                this.RSize = reader.ReadUInt32();
                uint dSize = reader.ReadUInt32();
                uint tSize = reader.ReadUInt32();
                uint fSize = reader.ReadUInt32();
                reader.ReadUInt32(); //dummy

                string charSet = StrUtils.GetString(reader.ReadBytes(32), Encoding.ASCII);
                this.encoding = StrUtils.GetEncoding(charSet);

                long offset = stream.Position;
                this.da.Open(mmf, offset, dSize);
                offset += dSize;
                this.tokens = mmf.CreateViewAccessor(offset, tSize, MemoryMappedFileAccess.Read);
                offset += tSize;
                this.features = mmf.CreateViewAccessor(offset, fSize, MemoryMappedFileAccess.Read);
            }
        }

        public unsafe DoubleArrayResultPair ExactMatchSearch(string key)
        {
            fixed (char* pKey = key)
                return this.ExactMatchSearch(pKey, key.Length, 0);
        }

        public unsafe DoubleArrayResultPair ExactMatchSearch(char* key, int len, int nodePos = 0)
        {
            //if (this.encoding == Encoding.Unicode)
            //    return this.da.ExactMatchSearch((byte*)key, len, nodePos);

            //エンコード
            int maxByteCount = this.encoding.GetMaxByteCount(len);
            byte* bytes = stackalloc byte[maxByteCount];
            int bytesLen = this.encoding.GetBytes(key, len, bytes, maxByteCount);

            DoubleArrayResultPair result = this.da.ExactMatchSearch(bytes, bytesLen, nodePos);

            //文字数をデコードしたものに変換
            result.Length = this.encoding.GetCharCount(bytes, result.Length);

            return result;
        }

        public unsafe int CommonPrefixSearch(char* key, int len, DoubleArrayResultPair* result, int rLen)
        {
            //if (this.encoding == Encoding.Unicode)
            //    return this.da.CommonPrefixSearch((byte*)key, result, rLen, len);

            //エンコード
            int maxByteLen = this.encoding.GetMaxByteCount(len);
            byte* bytes = stackalloc byte[maxByteLen];
            int bytesLen = this.encoding.GetBytes(key, len, bytes, maxByteLen);

            int n = this.da.CommonPrefixSearch(bytes, result, rLen, bytesLen);

            //文字数をデコードしたものに変換
            for (int i = 0; i < n; i++)
                result[i].Length = this.encoding.GetCharCount(bytes, result[i].Length);

            return n;
        }

        public unsafe Token[] GetToken(DoubleArrayResultPair n)
        {
            Token[] dist = new Token[0xFF & n.Value];
            int tokenPos = n.Value >> 8;

            this.tokens.ReadArray<Token>(tokenPos * sizeof(Token), dist, 0, dist.Length);
            return dist;
        }

        public string GetFeature(uint featurePos)
        {
            return StrUtils.GetString(this.features, (long)featurePos, this.encoding);
        }

        public bool IsCompatible(IMeCabDictionary d)
        {
            return (this.Version == d.Version &&
                    this.LSize == d.LSize &&
                    this.RSize == d.RSize &&
                    this.CharSet == d.CharSet);
        }

        private bool disposed;

        /// <summary>
        /// 使用されているリソースを開放する
        /// </summary>
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
                if (this.da != null) this.da.Dispose();
                if (this.mmf != null) this.mmf.Dispose();
                if (this.tokens != null) this.tokens.Dispose();
                if (this.features != null) this.features.Dispose();
            }

            this.disposed = true;
        }

        ~MeCabDictionaryMMF()
        {
            this.Dispose(false);
        }
    }
}
