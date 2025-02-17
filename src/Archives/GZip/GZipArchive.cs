﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SharpCompress.Common;
using SharpCompress.Common.GZip;
using SharpCompress.Readers;
using SharpCompress.Readers.GZip;
using SharpCompress.Writers;
using SharpCompress.Writers.GZip;

namespace SharpCompress.Archives.GZip
{
    public class GZipArchive : AbstractWritableArchive<GZipArchiveEntry, GZipVolume>
    {
        /// <summary>
        /// Constructor expects a filepath to an existing file.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="readerOptions"></param>
        public static GZipArchive Open(string filePath, ReaderOptions readerOptions = null)
        {
            filePath.CheckNotNullOrEmpty(nameof(filePath));
            return Open(new FileInfo(filePath), readerOptions ?? new ReaderOptions());
        }

        /// <summary>
        /// Constructor with a FileInfo object to an existing file.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="readerOptions"></param>
        public static GZipArchive Open(FileInfo fileInfo, ReaderOptions readerOptions = null)
        {
            fileInfo.CheckNotNull(nameof(fileInfo));
            return new GZipArchive(fileInfo, readerOptions ?? new ReaderOptions());
        }

        /// <summary>
        /// Takes a seekable Stream as a source
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="readerOptions"></param>
        public static GZipArchive Open(Stream stream, ReaderOptions readerOptions = null)
        {
            stream.CheckNotNull(nameof(stream));
            return new GZipArchive(stream, readerOptions ?? new ReaderOptions());
        }

        public static GZipArchive Create()
        {
            return new GZipArchive();
        }

        /// <summary>
        /// Constructor with a FileInfo object to an existing file.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="options"></param>
        internal GZipArchive(FileInfo fileInfo, ReaderOptions options)
            : base(ArchiveType.GZip, fileInfo, options)
        {
        }

        protected override IEnumerable<GZipVolume> LoadVolumes(FileInfo file)
        {
            return new GZipVolume(file, ReaderOptions).AsEnumerable();
        }

        public static bool IsGZipFile(string filePath)
        {
            return IsGZipFile(new FileInfo(filePath));
        }

        public static bool IsGZipFile(FileInfo fileInfo)
        {
            if (!fileInfo.Exists)
            {
                return false;
            }

             Stream stream = fileInfo.OpenRead();
            return IsGZipFile(stream);
        }

        public void SaveTo(string filePath)
        {
            SaveTo(new FileInfo(filePath));
        }

        public void SaveTo(FileInfo fileInfo)
        {
            using (var stream = fileInfo.Open(FileMode.Create, FileAccess.Write))
            {
                SaveTo(stream, new WriterOptions(CompressionType.GZip));
            }
        }

        public static bool IsGZipFile(Stream stream)
        {
            // read the header on the first read
            Span<byte> header = new byte[10];

            // workitem 8501: handle edge case (decompress empty stream)
            if (!stream.ReadFully(header))
            {
                return false;
            }

            if (header[0] != 0x1F || header[1] != 0x8B || header[2] != 8)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Takes multiple seekable Streams for a multi-part archive
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="options"></param>
        internal GZipArchive(Stream stream, ReaderOptions options)
            : base(ArchiveType.GZip, stream, options)
        {
        }

        internal GZipArchive()
            : base(ArchiveType.GZip)
        {
        }

        protected override GZipArchiveEntry CreateEntryInternal(string filePath, Stream source, long size, DateTime? modified,
                                                                bool closeStream)
        {
            if (Entries.Any())
            {
                throw new InvalidOperationException("Only one entry is allowed in a GZip Archive");
            }
            return new GZipWritableArchiveEntry(this, source, filePath, size, modified, closeStream);
        }

        protected override void SaveTo(Stream stream, WriterOptions options,
                                       IEnumerable<GZipArchiveEntry> oldEntries,
                                       IEnumerable<GZipArchiveEntry> newEntries, IProgress<Dictionary<string, long>> progress = null, CancellationTokenSource cancellationTokenSource = null)
        {
            if (Entries.Count > 1)
            {
                throw new InvalidOperationException("Only one entry is allowed in a GZip Archive");
            }
            using (var writer = new GZipWriter(stream, new GZipWriterOptions(options)))
            {
                foreach (var entry in oldEntries.Concat(newEntries)
                                                .Where(x => !x.IsDirectory))
                {
                    if (cancellationTokenSource != null)
                    {
                        if (cancellationTokenSource.IsCancellationRequested)
                        {
                            break;
                        }
                    }
                    using (var entryStream = entry.OpenEntryStream())
                    {
                        writer.Write(entry.Key, entryStream, entry.LastModifiedTime);
                    }
                }
            }
        }

        protected override IEnumerable<GZipVolume> LoadVolumes(IEnumerable<Stream> streams)
        {
            return new GZipVolume(streams.First(), ReaderOptions).AsEnumerable();
        }

        protected override IEnumerable<GZipArchiveEntry> LoadEntries(IEnumerable<GZipVolume> volumes)
        {
            Stream stream = volumes.Single().Stream;
            yield return new GZipArchiveEntry(this, new GZipFilePart(stream, ReaderOptions.ArchiveEncoding));
        }

        protected override IReader CreateReaderForSolidExtraction()
        {
            var stream = Volumes.Single().Stream;
            stream.Position = 0;
            return GZipReader.Open(stream);
        }
    }
}
