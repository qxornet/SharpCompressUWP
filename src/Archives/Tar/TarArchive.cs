﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SharpCompress.Common;
using SharpCompress.Common.Tar;
using SharpCompress.Common.Tar.Headers;
using SharpCompress.IO;
using SharpCompress.Readers;
using SharpCompress.Readers.Tar;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

namespace SharpCompress.Archives.Tar
{
    public class TarArchive : AbstractWritableArchive<TarArchiveEntry, TarVolume>
    {
        /// <summary>
        /// Constructor expects a filepath to an existing file.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="readerOptions"></param>
        public static TarArchive Open(string filePath, ReaderOptions readerOptions = null)
        {
            filePath.CheckNotNullOrEmpty(nameof(filePath));
            return Open(new FileInfo(filePath), readerOptions ?? new ReaderOptions());
        }

        /// <summary>
        /// Constructor with a FileInfo object to an existing file.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="readerOptions"></param>
        public static TarArchive Open(FileInfo fileInfo, ReaderOptions readerOptions = null)
        {
            fileInfo.CheckNotNull(nameof(fileInfo));
            return new TarArchive(fileInfo, readerOptions ?? new ReaderOptions());
        }

        /// <summary>
        /// Takes a seekable Stream as a source
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="readerOptions"></param>
        public static TarArchive Open(Stream stream, ReaderOptions readerOptions = null)
        {
            stream.CheckNotNull(nameof(stream));
            return new TarArchive(stream, readerOptions ?? new ReaderOptions());
        }

        public static bool IsTarFile(string filePath)
        {
            return IsTarFile(new FileInfo(filePath));
        }

        public static bool IsTarFile(FileInfo fileInfo)
        {
            if (!fileInfo.Exists)
            {
                return false;
            }
            using (Stream stream = fileInfo.OpenRead())
            {
                return IsTarFile(stream);
            }
        }

        public static bool IsTarFile(Stream stream)
        {
            try
            {
                TarHeader tarHeader = new TarHeader(new ArchiveEncoding());
                bool readSucceeded = tarHeader.Read(new BinaryReader(stream));
                bool isEmptyArchive = tarHeader.Name.Length == 0 && tarHeader.Size == 0 && Enum.IsDefined(typeof(EntryType), tarHeader.EntryType);
                return readSucceeded || isEmptyArchive;
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Constructor with a FileInfo object to an existing file.
        /// </summary>
        /// <param name="fileInfo"></param>
        /// <param name="readerOptions"></param>
        internal TarArchive(FileInfo fileInfo, ReaderOptions readerOptions)
            : base(ArchiveType.Tar, fileInfo, readerOptions)
        {
        }

        protected override IEnumerable<TarVolume> LoadVolumes(FileInfo file)
        {
            return new TarVolume(file.OpenRead(), ReaderOptions).AsEnumerable();
        }

        /// <summary>
        /// Takes multiple seekable Streams for a multi-part archive
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="readerOptions"></param>
        internal TarArchive(Stream stream, ReaderOptions readerOptions)
            : base(ArchiveType.Tar, stream, readerOptions)
        {
        }

        internal TarArchive()
            : base(ArchiveType.Tar)
        {
        }

        protected override IEnumerable<TarVolume> LoadVolumes(IEnumerable<Stream> streams)
        {
            return new TarVolume(streams.First(), ReaderOptions).AsEnumerable();
        }

        protected override IEnumerable<TarArchiveEntry> LoadEntries(IEnumerable<TarVolume> volumes)
        {
            Stream stream = volumes.Single().Stream;
            TarHeader previousHeader = null;
            foreach (TarHeader header in TarHeaderFactory.ReadHeader(StreamingMode.Seekable, stream, ReaderOptions.ArchiveEncoding))
            {
                if (header != null)
                {
                    if (header.EntryType == EntryType.LongName)
                    {
                        previousHeader = header;
                    }
                    else
                    {
                        if (previousHeader != null)
                        {
                            var entry = new TarArchiveEntry(this, new TarFilePart(previousHeader, stream),
                                                            CompressionType.None);

                            var oldStreamPos = stream.Position;

                            using (var entryStream = entry.OpenEntryStream())
                            {
                                using (var memoryStream = new MemoryStream())
                                {
                                    entryStream.TransferTo(memoryStream);
                                    memoryStream.Position = 0;
                                    var bytes = memoryStream.ToArray();

                                    header.Name = ReaderOptions.ArchiveEncoding.Decode(bytes).TrimNulls();
                                }
                            }

                            stream.Position = oldStreamPos;

                            previousHeader = null;
                        }
                        yield return new TarArchiveEntry(this, new TarFilePart(header, stream), CompressionType.None);
                    }
                }
            }
        }

        public static TarArchive Create()
        {
            return new TarArchive();
        }

        protected override TarArchiveEntry CreateEntryInternal(string filePath, Stream source,
                                                               long size, DateTime? modified, bool closeStream)
        {
            return new TarWritableArchiveEntry(this, source, CompressionType.Unknown, filePath, size, modified,
                                               closeStream);
        }

        protected override void SaveTo(Stream stream, WriterOptions options,
                                       IEnumerable<TarArchiveEntry> oldEntries,
                                       IEnumerable<TarArchiveEntry> newEntries, IProgress<Dictionary<string, long>> progress = null, CancellationTokenSource cancellationTokenSource = null)
        {
            using (var writer = new TarWriter(stream, new TarWriterOptions(options)))
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
                        writer.Write(entry.Key, entryStream, entry.LastModifiedTime, entry.Size);
                    }
                }
            }
        }

        protected override IReader CreateReaderForSolidExtraction()
        {
            var stream = Volumes.Single().Stream;
            stream.Position = 0;
            return TarReader.Open(stream);
        }
    }
}
