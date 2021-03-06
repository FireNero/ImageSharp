﻿// <copyright file="PngDecoderCore.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace ImageSharp.Formats
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;

    using ImageSharp.PixelFormats;

    using static ComparableExtensions;

    /// <summary>
    /// Performs the png decoding operation.
    /// </summary>
    internal class PngDecoderCore
    {
        /// <summary>
        /// The dictionary of available color types.
        /// </summary>
        private static readonly Dictionary<PngColorType, byte[]> ColorTypes = new Dictionary<PngColorType, byte[]>()
        {
            [PngColorType.Grayscale] = new byte[] { 1, 2, 4, 8 },
            [PngColorType.Rgb] = new byte[] { 8 },
            [PngColorType.Palette] = new byte[] { 1, 2, 4, 8 },
            [PngColorType.GrayscaleWithAlpha] = new byte[] { 8 },
            [PngColorType.RgbWithAlpha] = new byte[] { 8 },
        };

        /// <summary>
        /// The amount to increment when processing each column per scanline for each interlaced pass
        /// </summary>
        private static readonly int[] Adam7ColumnIncrement = { 8, 8, 4, 4, 2, 2, 1 };

        /// <summary>
        /// The index to start at when processing each column per scanline for each interlaced pass
        /// </summary>
        private static readonly int[] Adam7FirstColumn = { 0, 4, 0, 2, 0, 1, 0 };

        /// <summary>
        /// The index to start at when processing each row per scanline for each interlaced pass
        /// </summary>
        private static readonly int[] Adam7FirstRow = { 0, 0, 4, 0, 2, 0, 1 };

        /// <summary>
        /// The amount to increment when processing each row per scanline for each interlaced pass
        /// </summary>
        private static readonly int[] Adam7RowIncrement = { 8, 8, 8, 4, 4, 2, 2 };

        /// <summary>
        /// Reusable buffer for reading chunk types.
        /// </summary>
        private readonly byte[] chunkTypeBuffer = new byte[4];

        /// <summary>
        /// Reusable buffer for reading chunk lengths.
        /// </summary>
        private readonly byte[] chunkLengthBuffer = new byte[4];

        /// <summary>
        /// Reusable buffer for reading crc values.
        /// </summary>
        private readonly byte[] crcBuffer = new byte[4];

        /// <summary>
        /// Reusable buffer for reading char arrays.
        /// </summary>
        private readonly char[] chars = new char[4];

        /// <summary>
        /// The decoder options.
        /// </summary>
        private readonly IPngDecoderOptions options;

        /// <summary>
        /// Reusable crc for validating chunks.
        /// </summary>
        private readonly Crc32 crc = new Crc32();

        /// <summary>
        /// The global configuration.
        /// </summary>
        private readonly Configuration configuration;

        /// <summary>
        /// The stream to decode from.
        /// </summary>
        private Stream currentStream;

        /// <summary>
        /// The png header.
        /// </summary>
        private PngHeader header;

        /// <summary>
        /// The number of bytes per pixel.
        /// </summary>
        private int bytesPerPixel;

        /// <summary>
        /// The number of bytes per sample
        /// </summary>
        private int bytesPerSample;

        /// <summary>
        /// The number of bytes per scanline
        /// </summary>
        private int bytesPerScanline;

        /// <summary>
        /// The palette containing color information for indexed png's
        /// </summary>
        private byte[] palette;

        /// <summary>
        /// The palette containing alpha channel color information for indexed png's
        /// </summary>
        private byte[] paletteAlpha;

        /// <summary>
        /// A value indicating whether the end chunk has been reached.
        /// </summary>
        private bool isEndChunkReached;

        /// <summary>
        /// Previous scanline processed
        /// </summary>
        private byte[] previousScanline;

        /// <summary>
        /// The current scanline that is being processed
        /// </summary>
        private byte[] scanline;

        /// <summary>
        /// The index of the current scanline being processed
        /// </summary>
        private int currentRow = Adam7FirstRow[0];

        /// <summary>
        /// The current pass for an interlaced PNG
        /// </summary>
        private int pass = 0;

        /// <summary>
        /// The current number of bytes read in the current scanline
        /// </summary>
        private int currentRowBytesRead = 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="PngDecoderCore"/> class.
        /// </summary>
        /// <param name="options">The decoder options.</param>
        /// <param name="configuration">The configuration.</param>
        public PngDecoderCore(IPngDecoderOptions options, Configuration configuration)
        {
            this.configuration = configuration ?? Configuration.Default;
            this.options = options ?? new PngDecoderOptions();
        }

        /// <summary>
        /// Gets or sets the png color type
        /// </summary>
        public PngColorType PngColorType { get; set; }

        /// <summary>
        /// Decodes the stream to the image.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="stream">The stream containing image data. </param>
        /// <exception cref="ImageFormatException">
        /// Thrown if the stream does not contain and end chunk.
        /// </exception>
        /// <exception cref="System.ArgumentOutOfRangeException">
        /// Thrown if the image is larger than the maximum allowable size.
        /// </exception>
        /// <returns>The decoded image</returns>
        public Image<TPixel> Decode<TPixel>(Stream stream)
            where TPixel : struct, IPixel<TPixel>
        {
            ImageMetaData metadata = new ImageMetaData();
            this.currentStream = stream;
            this.currentStream.Skip(8);
            Image<TPixel> image = null;
            PixelAccessor<TPixel> pixels = null;
            try
            {
                using (ZlibInflateStream deframeStream = new ZlibInflateStream(this.currentStream))
                {
                    PngChunk currentChunk;
                    while (!this.isEndChunkReached && (currentChunk = this.ReadChunk()) != null)
                    {
                        try
                        {
                            switch (currentChunk.Type)
                            {
                                case PngChunkTypes.Header:
                                    this.ReadHeaderChunk(currentChunk.Data);
                                    this.ValidateHeader();
                                    break;
                                case PngChunkTypes.Physical:
                                    this.ReadPhysicalChunk(metadata, currentChunk.Data);
                                    break;
                                case PngChunkTypes.Data:
                                    if (image == null)
                                    {
                                        this.InitializeImage(metadata, out image, out pixels);
                                    }

                                    deframeStream.AllocateNewBytes(currentChunk.Length);
                                    this.ReadScanlines(deframeStream.CompressedStream, pixels);
                                    stream.Read(this.crcBuffer, 0, 4);
                                    break;
                                case PngChunkTypes.Palette:
                                    byte[] pal = new byte[currentChunk.Length];
                                    Buffer.BlockCopy(currentChunk.Data, 0, pal, 0, currentChunk.Length);
                                    this.palette = pal;
                                    metadata.Quality = pal.Length / 3;
                                    break;
                                case PngChunkTypes.PaletteAlpha:
                                    byte[] alpha = new byte[currentChunk.Length];
                                    Buffer.BlockCopy(currentChunk.Data, 0, alpha, 0, currentChunk.Length);
                                    this.paletteAlpha = alpha;
                                    break;
                                case PngChunkTypes.Text:
                                    this.ReadTextChunk(metadata, currentChunk.Data, currentChunk.Length);
                                    break;
                                case PngChunkTypes.End:
                                    this.isEndChunkReached = true;
                                    break;
                            }
                        }
                        finally
                        {
                            // Data is rented in ReadChunkData()
                            if (currentChunk.Data != null)
                            {
                                ArrayPool<byte>.Shared.Return(currentChunk.Data);
                            }
                        }
                    }
                }

                return image;
            }
            finally
            {
                pixels?.Dispose();
                if (this.previousScanline != null)
                {
                    ArrayPool<byte>.Shared.Return(this.previousScanline);
                    ArrayPool<byte>.Shared.Return(this.scanline);
                }
            }
        }

        /// <summary>
        /// Converts a byte array to a new array where each value in the original array is represented by the specified number of bits.
        /// </summary>
        /// <param name="source">The bytes to convert from. Cannot be null.</param>
        /// <param name="bytesPerScanline">The number of bytes per scanline</param>
        /// <param name="bits">The number of bits per value.</param>
        /// <returns>The resulting <see cref="T:byte[]"/> array. Is never null.</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="source"/> is null.</exception>
        /// <exception cref="System.ArgumentException"><paramref name="bits"/> is less than or equals than zero.</exception>
        private static byte[] ToArrayByBitsLength(byte[] source, int bytesPerScanline, int bits)
        {
            Guard.NotNull(source, nameof(source));
            Guard.MustBeGreaterThan(bits, 0, nameof(bits));

            byte[] result;

            if (bits < 8)
            {
                result = new byte[bytesPerScanline * 8 / bits];
                int mask = 0xFF >> (8 - bits);
                int resultOffset = 0;

                // ReSharper disable once ForCanBeConvertedToForeach
                // First byte is the marker so skip.
                for (int i = 1; i < bytesPerScanline; i++)
                {
                    byte b = source[i];
                    for (int shift = 0; shift < 8; shift += bits)
                    {
                        int colorIndex = (b >> (8 - bits - shift)) & mask;

                        result[resultOffset] = (byte)colorIndex;

                        resultOffset++;
                    }
                }
            }
            else
            {
                result = source;
            }

            return result;
        }

        /// <summary>
        /// Reads the data chunk containing physical dimension data.
        /// </summary>
        /// <param name="metadata">The metadata to read to.</param>
        /// <param name="data">The data containing physical data.</param>
        private void ReadPhysicalChunk(ImageMetaData metadata, byte[] data)
        {
            data.ReverseBytes(0, 4);
            data.ReverseBytes(4, 4);

            // 39.3700787 = inches in a meter.
            metadata.HorizontalResolution = BitConverter.ToInt32(data, 0) / 39.3700787d;
            metadata.VerticalResolution = BitConverter.ToInt32(data, 4) / 39.3700787d;
        }

        /// <summary>
        /// Initializes the image and various buffers needed for processing
        /// </summary>
        /// <typeparam name="TPixel">The type the pixels will be</typeparam>
        /// <param name="metadata">The metadata information for the image</param>
        /// <param name="image">The image that we will populate</param>
        /// <param name="pixels">The pixel accessor</param>
        private void InitializeImage<TPixel>(ImageMetaData metadata, out Image<TPixel> image, out PixelAccessor<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            if (this.header.Width > Image<TPixel>.MaxWidth || this.header.Height > Image<TPixel>.MaxHeight)
            {
                throw new ArgumentOutOfRangeException($"The input png '{this.header.Width}x{this.header.Height}' is bigger than the max allowed size '{Image<TPixel>.MaxWidth}x{Image<TPixel>.MaxHeight}'");
            }

            image = new Image<TPixel>(this.configuration, this.header.Width, this.header.Height, metadata);
            pixels = image.Lock();
            this.bytesPerPixel = this.CalculateBytesPerPixel();
            this.bytesPerScanline = this.CalculateScanlineLength(this.header.Width) + 1;
            this.bytesPerSample = 1;
            if (this.header.BitDepth >= 8)
            {
                this.bytesPerSample = this.header.BitDepth / 8;
            }

            this.previousScanline = ArrayPool<byte>.Shared.Rent(this.bytesPerScanline);
            this.scanline = ArrayPool<byte>.Shared.Rent(this.bytesPerScanline);

            // Zero out the scanlines, because the bytes that are rented from the arraypool may not be zero.
            Array.Clear(this.scanline, 0, this.bytesPerScanline);
            Array.Clear(this.previousScanline, 0, this.bytesPerScanline);
        }

        /// <summary>
        /// Calculates the correct number of bytes per pixel for the given color type.
        /// </summary>
        /// <returns>The <see cref="int"/></returns>
        private int CalculateBytesPerPixel()
        {
            switch (this.PngColorType)
            {
                case PngColorType.Grayscale:
                    return 1;

                case PngColorType.GrayscaleWithAlpha:
                    return 2;

                case PngColorType.Palette:
                    return 1;

                case PngColorType.Rgb:
                    return 3;

                // PngColorType.RgbWithAlpha:
                default:
                    return 4;
            }
        }

        /// <summary>
        /// Calculates the scanline length.
        /// </summary>
        /// <param name="width">The width of the row.</param>
        /// <returns>
        /// The <see cref="int"/> representing the length.
        /// </returns>
        private int CalculateScanlineLength(int width)
        {
            int scanlineLength = width * this.header.BitDepth * this.bytesPerPixel;

            int amount = scanlineLength % 8;
            if (amount != 0)
            {
                scanlineLength += 8 - amount;
            }

            return scanlineLength / 8;
        }

        /// <summary>
        /// Reads the scanlines within the image.
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="dataStream">The <see cref="MemoryStream"/> containing data.</param>
        /// <param name="pixels"> The pixel data.</param>
        private void ReadScanlines<TPixel>(Stream dataStream, PixelAccessor<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            if (this.header.InterlaceMethod == PngInterlaceMode.Adam7)
            {
                this.DecodeInterlacedPixelData(dataStream, pixels);
            }
            else
            {
                this.DecodePixelData(dataStream, pixels);
            }
        }

        /// <summary>
        /// Decodes the raw pixel data row by row
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="compressedStream">The compressed pixel data stream.</param>
        /// <param name="pixels">The image pixel accessor.</param>
        private void DecodePixelData<TPixel>(Stream compressedStream, PixelAccessor<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            while (this.currentRow < this.header.Height)
            {
                int bytesRead = compressedStream.Read(this.scanline, this.currentRowBytesRead, this.bytesPerScanline - this.currentRowBytesRead);
                this.currentRowBytesRead += bytesRead;
                if (this.currentRowBytesRead < this.bytesPerScanline)
                {
                    return;
                }

                this.currentRowBytesRead = 0;
                FilterType filterType = (FilterType)this.scanline[0];

                switch (filterType)
                {
                    case FilterType.None:

                        NoneFilter.Decode(this.scanline);

                        break;

                    case FilterType.Sub:

                        SubFilter.Decode(this.scanline, this.bytesPerScanline, this.bytesPerPixel);

                        break;

                    case FilterType.Up:

                        UpFilter.Decode(this.scanline, this.previousScanline, this.bytesPerScanline);

                        break;

                    case FilterType.Average:

                        AverageFilter.Decode(this.scanline, this.previousScanline, this.bytesPerScanline, this.bytesPerPixel);

                        break;

                    case FilterType.Paeth:

                        PaethFilter.Decode(this.scanline, this.previousScanline, this.bytesPerScanline, this.bytesPerPixel);

                        break;

                    default:
                        throw new ImageFormatException("Unknown filter type.");
                }

                this.ProcessDefilteredScanline(this.scanline, pixels);

                Swap(ref this.scanline, ref this.previousScanline);
                this.currentRow++;
            }
        }

        /// <summary>
        /// Decodes the raw interlaced pixel data row by row
        /// <see href="https://github.com/juehv/DentalImageViewer/blob/8a1a4424b15d6cc453b5de3f273daf3ff5e3a90d/DentalImageViewer/lib/jiu-0.14.3/net/sourceforge/jiu/codecs/PNGCodec.java"/>
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="compressedStream">The compressed pixel data stream.</param>
        /// <param name="pixels">The image pixel accessor.</param>
        private void DecodeInterlacedPixelData<TPixel>(Stream compressedStream, PixelAccessor<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            while (true)
            {
                int numColumns = this.ComputeColumnsAdam7(this.pass);

                if (numColumns == 0)
                {
                    this.pass++;

                    // This pass contains no data; skip to next pass
                    continue;
                }

                int bytesPerInterlaceScanline = this.CalculateScanlineLength(numColumns) + 1;

                while (this.currentRow < this.header.Height)
                {
                    int bytesRead = compressedStream.Read(this.scanline, this.currentRowBytesRead, bytesPerInterlaceScanline - this.currentRowBytesRead);
                    this.currentRowBytesRead += bytesRead;
                    if (this.currentRowBytesRead < bytesPerInterlaceScanline)
                    {
                        return;
                    }

                    this.currentRowBytesRead = 0;

                    FilterType filterType = (FilterType)this.scanline[0];

                    switch (filterType)
                    {
                        case FilterType.None:

                            NoneFilter.Decode(this.scanline);

                            break;

                        case FilterType.Sub:

                            SubFilter.Decode(this.scanline, bytesPerInterlaceScanline, this.bytesPerPixel);

                            break;

                        case FilterType.Up:

                            UpFilter.Decode(this.scanline, this.previousScanline, bytesPerInterlaceScanline);

                            break;

                        case FilterType.Average:

                            AverageFilter.Decode(this.scanline, this.previousScanline, bytesPerInterlaceScanline, this.bytesPerPixel);

                            break;

                        case FilterType.Paeth:

                            PaethFilter.Decode(this.scanline, this.previousScanline, bytesPerInterlaceScanline, this.bytesPerPixel);

                            break;

                        default:
                            throw new ImageFormatException("Unknown filter type.");
                    }

                    this.ProcessInterlacedDefilteredScanline(this.scanline, this.currentRow, pixels, Adam7FirstColumn[this.pass], Adam7ColumnIncrement[this.pass]);

                    Swap(ref this.scanline, ref this.previousScanline);

                    this.currentRow += Adam7RowIncrement[this.pass];
                }

                this.pass++;
                if (this.pass < 7)
                {
                    this.currentRow = Adam7FirstRow[this.pass];
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Processes the de-filtered scanline filling the image pixel data
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="defilteredScanline">The de-filtered scanline</param>
        /// <param name="pixels">The image pixels</param>
        private void ProcessDefilteredScanline<TPixel>(byte[] defilteredScanline, PixelAccessor<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            TPixel color = default(TPixel);
            BufferSpan<TPixel> pixelBuffer = pixels.GetRowSpan(this.currentRow);
            BufferSpan<byte> scanlineBuffer = new BufferSpan<byte>(defilteredScanline, 1);
            switch (this.PngColorType)
            {
                case PngColorType.Grayscale:
                    int factor = 255 / ((int)Math.Pow(2, this.header.BitDepth) - 1);
                    byte[] newScanline1 = ToArrayByBitsLength(defilteredScanline, this.bytesPerScanline, this.header.BitDepth);
                    for (int x = 0; x < this.header.Width; x++)
                    {
                        byte intensity = (byte)(newScanline1[x] * factor);
                        color.PackFromBytes(intensity, intensity, intensity, 255);
                        pixels[x, this.currentRow] = color;
                    }

                    break;

                case PngColorType.GrayscaleWithAlpha:

                    for (int x = 0; x < this.header.Width; x++)
                    {
                        int offset = 1 + (x * this.bytesPerPixel);

                        byte intensity = defilteredScanline[offset];
                        byte alpha = defilteredScanline[offset + this.bytesPerSample];

                        color.PackFromBytes(intensity, intensity, intensity, alpha);
                        pixels[x, this.currentRow] = color;
                    }

                    break;

                case PngColorType.Palette:

                    this.ProcessScanlineFromPalette(defilteredScanline, pixels);

                    break;

                case PngColorType.Rgb:

                    PixelOperations<TPixel>.Instance.PackFromXyzBytes(scanlineBuffer, pixelBuffer, this.header.Width);

                    break;

                case PngColorType.RgbWithAlpha:

                    PixelOperations<TPixel>.Instance.PackFromXyzwBytes(scanlineBuffer, pixelBuffer, this.header.Width);

                    break;
            }
        }

        /// <summary>
        /// Processes a scanline that uses a palette
        /// </summary>
        /// <typeparam name="TPixel">The type of pixel we are expanding to</typeparam>
        /// <param name="defilteredScanline">The scanline</param>
        /// <param name="pixels">The output pixels</param>
        private void ProcessScanlineFromPalette<TPixel>(byte[] defilteredScanline, PixelAccessor<TPixel> pixels)
            where TPixel : struct, IPixel<TPixel>
        {
            byte[] newScanline = ToArrayByBitsLength(defilteredScanline, this.bytesPerScanline, this.header.BitDepth);
            byte[] palette = this.palette;
            TPixel color = default(TPixel);

            if (this.paletteAlpha != null && this.paletteAlpha.Length > 0)
            {
                // If the alpha palette is not null and has one or more entries, this means, that the image contains an alpha
                // channel and we should try to read it.
                for (int x = 0; x < this.header.Width; x++)
                {
                    int index = newScanline[x + 1];
                    int pixelOffset = index * 3;

                    byte a = this.paletteAlpha.Length > index ? this.paletteAlpha[index] : (byte)255;

                    if (a > 0)
                    {
                        byte r = palette[pixelOffset];
                        byte g = palette[pixelOffset + 1];
                        byte b = palette[pixelOffset + 2];
                        color.PackFromBytes(r, g, b, a);
                    }
                    else
                    {
                        color.PackFromBytes(0, 0, 0, 0);
                    }

                    pixels[x, this.currentRow] = color;
                }
            }
            else
            {
                for (int x = 0; x < this.header.Width; x++)
                {
                    int index = newScanline[x + 1];
                    int pixelOffset = index * 3;

                    byte r = palette[pixelOffset];
                    byte g = palette[pixelOffset + 1];
                    byte b = palette[pixelOffset + 2];

                    color.PackFromBytes(r, g, b, 255);
                    pixels[x, this.currentRow] = color;
                }
            }
        }

        /// <summary>
        /// Processes the interlaced de-filtered scanline filling the image pixel data
        /// </summary>
        /// <typeparam name="TPixel">The pixel format.</typeparam>
        /// <param name="defilteredScanline">The de-filtered scanline</param>
        /// <param name="row">The current image row.</param>
        /// <param name="pixels">The image pixels</param>
        /// <param name="pixelOffset">The column start index. Always 0 for none interlaced images.</param>
        /// <param name="increment">The column increment. Always 1 for none interlaced images.</param>
        private void ProcessInterlacedDefilteredScanline<TPixel>(byte[] defilteredScanline, int row, PixelAccessor<TPixel> pixels, int pixelOffset = 0, int increment = 1)
            where TPixel : struct, IPixel<TPixel>
        {
            TPixel color = default(TPixel);

            switch (this.PngColorType)
            {
                case PngColorType.Grayscale:
                    int factor = 255 / ((int)Math.Pow(2, this.header.BitDepth) - 1);
                    byte[] newScanline1 = ToArrayByBitsLength(defilteredScanline, this.bytesPerScanline, this.header.BitDepth);
                    for (int x = pixelOffset, o = 1; x < this.header.Width; x += increment, o++)
                    {
                        byte intensity = (byte)(newScanline1[o] * factor);
                        color.PackFromBytes(intensity, intensity, intensity, 255);
                        pixels[x, row] = color;
                    }

                    break;

                case PngColorType.GrayscaleWithAlpha:

                    for (int x = pixelOffset, o = 1; x < this.header.Width; x += increment, o += this.bytesPerPixel)
                    {
                        byte intensity = defilteredScanline[o];
                        byte alpha = defilteredScanline[o + this.bytesPerSample];

                        color.PackFromBytes(intensity, intensity, intensity, alpha);
                        pixels[x, row] = color;
                    }

                    break;

                case PngColorType.Palette:

                    byte[] newScanline = ToArrayByBitsLength(defilteredScanline, this.bytesPerScanline, this.header.BitDepth);

                    if (this.paletteAlpha != null && this.paletteAlpha.Length > 0)
                    {
                        // If the alpha palette is not null and has one or more entries, this means, that the image contains an alpha
                        // channel and we should try to read it.
                        for (int x = pixelOffset, o = 1; x < this.header.Width; x += increment, o++)
                        {
                            int index = newScanline[o];
                            int offset = index * 3;

                            byte a = this.paletteAlpha.Length > index ? this.paletteAlpha[index] : (byte)255;

                            if (a > 0)
                            {
                                byte r = this.palette[offset];
                                byte g = this.palette[offset + 1];
                                byte b = this.palette[offset + 2];
                                color.PackFromBytes(r, g, b, a);
                            }
                            else
                            {
                                color.PackFromBytes(0, 0, 0, 0);
                            }

                            pixels[x, row] = color;
                        }
                    }
                    else
                    {
                        for (int x = pixelOffset, o = 1; x < this.header.Width; x += increment, o++)
                        {
                            int index = newScanline[o];
                            int offset = index * 3;

                            byte r = this.palette[offset];
                            byte g = this.palette[offset + 1];
                            byte b = this.palette[offset + 2];

                            color.PackFromBytes(r, g, b, 255);
                            pixels[x, row] = color;
                        }
                    }

                    break;

                case PngColorType.Rgb:

                    for (int x = pixelOffset, o = 1; x < this.header.Width; x += increment, o += this.bytesPerPixel)
                    {
                        byte r = defilteredScanline[o];
                        byte g = defilteredScanline[o + this.bytesPerSample];
                        byte b = defilteredScanline[o + (2 * this.bytesPerSample)];

                        color.PackFromBytes(r, g, b, 255);
                        pixels[x, row] = color;
                    }

                    break;

                case PngColorType.RgbWithAlpha:

                    for (int x = pixelOffset, o = 1; x < this.header.Width; x += increment, o += this.bytesPerPixel)
                    {
                        byte r = defilteredScanline[o];
                        byte g = defilteredScanline[o + this.bytesPerSample];
                        byte b = defilteredScanline[o + (2 * this.bytesPerSample)];
                        byte a = defilteredScanline[o + (3 * this.bytesPerSample)];

                        color.PackFromBytes(r, g, b, a);
                        pixels[x, row] = color;
                    }

                    break;
            }
        }

        /// <summary>
        /// Reads a text chunk containing image properties from the data.
        /// </summary>
        /// <param name="metadata">The metadata to decode to.</param>
        /// <param name="data">The <see cref="T:byte[]"/> containing  data.</param>
        /// <param name="length">The maximum length to read.</param>
        private void ReadTextChunk(ImageMetaData metadata, byte[] data, int length)
        {
            if (this.options.IgnoreMetadata)
            {
                return;
            }

            int zeroIndex = 0;

            for (int i = 0; i < length; i++)
            {
                if (data[i] == 0)
                {
                    zeroIndex = i;
                    break;
                }
            }

            string name = this.options.TextEncoding.GetString(data, 0, zeroIndex);
            string value = this.options.TextEncoding.GetString(data, zeroIndex + 1, length - zeroIndex - 1);

            metadata.Properties.Add(new ImageProperty(name, value));
        }

        /// <summary>
        /// Reads a header chunk from the data.
        /// </summary>
        /// <param name="data">The <see cref="T:byte[]"/> containing  data.</param>
        private void ReadHeaderChunk(byte[] data)
        {
            this.header = new PngHeader();

            data.ReverseBytes(0, 4);
            data.ReverseBytes(4, 4);

            this.header.Width = BitConverter.ToInt32(data, 0);
            this.header.Height = BitConverter.ToInt32(data, 4);

            this.header.BitDepth = data[8];
            this.header.ColorType = (PngColorType)data[9];
            this.header.CompressionMethod = data[10];
            this.header.FilterMethod = data[11];
            this.header.InterlaceMethod = (PngInterlaceMode)data[12];
        }

        /// <summary>
        /// Validates the png header.
        /// </summary>
        /// <exception cref="NotSupportedException">
        /// Thrown if the image does pass validation.
        /// </exception>
        private void ValidateHeader()
        {
            if (!ColorTypes.ContainsKey(this.header.ColorType))
            {
                throw new NotSupportedException("Color type is not supported or not valid.");
            }

            if (!ColorTypes[this.header.ColorType].Contains(this.header.BitDepth))
            {
                throw new NotSupportedException("Bit depth is not supported or not valid.");
            }

            if (this.header.FilterMethod != 0)
            {
                throw new NotSupportedException("The png specification only defines 0 as filter method.");
            }

            if (this.header.InterlaceMethod != PngInterlaceMode.None && this.header.InterlaceMethod != PngInterlaceMode.Adam7)
            {
                throw new NotSupportedException("The png specification only defines 'None' and 'Adam7' as interlaced methods.");
            }

            this.PngColorType = (PngColorType)this.header.ColorType;
        }

        /// <summary>
        /// Reads a chunk from the stream.
        /// </summary>
        /// <returns>
        /// The <see cref="PngChunk"/>.
        /// </returns>
        private PngChunk ReadChunk()
        {
            PngChunk chunk = new PngChunk();
            this.ReadChunkLength(chunk);
            if (chunk.Length < 0)
            {
                return null;
            }

            this.ReadChunkType(chunk);
            if (chunk.Type == PngChunkTypes.Data)
            {
                return chunk;
            }

            this.ReadChunkData(chunk);
            this.ReadChunkCrc(chunk);

            return chunk;
        }

        /// <summary>
        /// Reads the cycle redundancy chunk from the data.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        /// <exception cref="ImageFormatException">
        /// Thrown if the input stream is not valid or corrupt.
        /// </exception>
        private void ReadChunkCrc(PngChunk chunk)
        {
            int numBytes = this.currentStream.Read(this.crcBuffer, 0, 4);
            if (numBytes >= 1 && numBytes <= 3)
            {
                throw new ImageFormatException("Image stream is not valid!");
            }

            this.crcBuffer.ReverseBytes();

            chunk.Crc = BitConverter.ToUInt32(this.crcBuffer, 0);

            this.crc.Reset();
            this.crc.Update(this.chunkTypeBuffer);
            this.crc.Update(chunk.Data, 0, chunk.Length);

            if (this.crc.Value != chunk.Crc)
            {
                throw new ImageFormatException("CRC Error. PNG Image chunk is corrupt!");
            }
        }

        /// <summary>
        /// Reads the chunk data from the stream.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        private void ReadChunkData(PngChunk chunk)
        {
            // We rent the buffer here to return it afterwards in Decode()
            chunk.Data = ArrayPool<byte>.Shared.Rent(chunk.Length);
            this.currentStream.Read(chunk.Data, 0, chunk.Length);
        }

        /// <summary>
        /// Identifies the chunk type from the chunk.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        /// <exception cref="ImageFormatException">
        /// Thrown if the input stream is not valid.
        /// </exception>
        private void ReadChunkType(PngChunk chunk)
        {
            int numBytes = this.currentStream.Read(this.chunkTypeBuffer, 0, 4);
            if (numBytes >= 1 && numBytes <= 3)
            {
                throw new ImageFormatException("Image stream is not valid!");
            }

            this.chars[0] = (char)this.chunkTypeBuffer[0];
            this.chars[1] = (char)this.chunkTypeBuffer[1];
            this.chars[2] = (char)this.chunkTypeBuffer[2];
            this.chars[3] = (char)this.chunkTypeBuffer[3];

            chunk.Type = new string(this.chars);
        }

        /// <summary>
        /// Calculates the length of the given chunk.
        /// </summary>
        /// <param name="chunk">The chunk.</param>
        /// <exception cref="ImageFormatException">
        /// Thrown if the input stream is not valid.
        /// </exception>
        private void ReadChunkLength(PngChunk chunk)
        {
            int numBytes = this.currentStream.Read(this.chunkLengthBuffer, 0, 4);
            if (numBytes < 4)
            {
                chunk.Length = -1;
                return;
            }

            this.chunkLengthBuffer.ReverseBytes();

            chunk.Length = BitConverter.ToInt32(this.chunkLengthBuffer, 0);
        }

        /// <summary>
        /// Returns the correct number of columns for each interlaced pass.
        /// </summary>
        /// <param name="pass">Th current pass index</param>
        /// <returns>The <see cref="int"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ComputeColumnsAdam7(int pass)
        {
            int width = this.header.Width;
            switch (pass)
            {
                case 0: return (width + 7) / 8;
                case 1: return (width + 3) / 8;
                case 2: return (width + 3) / 4;
                case 3: return (width + 1) / 4;
                case 4: return (width + 1) / 2;
                case 5: return width / 2;
                case 6: return width;
                default: throw new ArgumentException($"Not a valid pass index: {pass}");
            }
        }
    }
}
