﻿// <copyright file="HalfSingle.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace ImageSharp.PixelFormats
{
    using System.Numerics;
    using System.Runtime.CompilerServices;

    /// <summary>
    /// Packed pixel type containing a single 16 bit floating point value.
    /// <para>
    /// Ranges from &lt;0, 0, 0, 1&gt; to &lt;1, 0, 0, 1&gt; in vector form.
    /// </para>
    /// </summary>
    public struct HalfSingle : IPixel<HalfSingle>, IPackedVector<ushort>
    {
        /// <summary>
        /// The maximum byte value.
        /// </summary>
        private static readonly Vector4 MaxBytes = new Vector4(255);

        /// <summary>
        /// The half vector value.
        /// </summary>
        private static readonly Vector4 Half = new Vector4(0.5F);

        /// <summary>
        /// Initializes a new instance of the <see cref="HalfSingle"/> struct.
        /// </summary>
        /// <param name="single">The single component.</param>
        public HalfSingle(float single)
        {
            this.PackedValue = HalfTypeHelper.Pack(single);
        }

        /// <inheritdoc/>
        public ushort PackedValue { get; set; }

        /// <summary>
        /// Compares two <see cref="HalfSingle"/> objects for equality.
        /// </summary>
        /// <param name="left">
        /// The <see cref="HalfSingle"/> on the left side of the operand.
        /// </param>
        /// <param name="right">
        /// The <see cref="HalfSingle"/> on the right side of the operand.
        /// </param>
        /// <returns>
        /// True if the <paramref name="left"/> parameter is equal to the <paramref name="right"/> parameter; otherwise, false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(HalfSingle left, HalfSingle right)
        {
            return left.PackedValue == right.PackedValue;
        }

        /// <summary>
        /// Compares two <see cref="HalfSingle"/> objects for equality.
        /// </summary>
        /// <param name="left">
        /// The <see cref="HalfSingle"/> on the left side of the operand.
        /// </param>
        /// <param name="right">
        /// The <see cref="HalfSingle"/> on the right side of the operand.
        /// </param>
        /// <returns>
        /// True if the <paramref name="left"/> parameter is not equal to the <paramref name="right"/> parameter; otherwise, false.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(HalfSingle left, HalfSingle right)
        {
            return left.PackedValue != right.PackedValue;
        }

        /// <inheritdoc />
        public PixelOperations<HalfSingle> CreateBulkOperations() => new PixelOperations<HalfSingle>();

        /// <summary>
        /// Expands the packed representation into a <see cref="float"/>.
        /// </summary>
        /// <returns>The <see cref="float"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToSingle()
        {
            return HalfTypeHelper.Unpack(this.PackedValue);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PackFromVector4(Vector4 vector)
        {
            this.PackedValue = HalfTypeHelper.Pack(vector.X);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector4 ToVector4()
        {
            return new Vector4(this.ToSingle(), 0, 0, 1);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PackFromBytes(byte x, byte y, byte z, byte w)
        {
            this.PackFromVector4(new Vector4(x, y, z, w) / MaxBytes);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToXyzBytes(byte[] bytes, int startIndex)
        {
            Vector4 vector = this.ToVector4();
            vector *= MaxBytes;
            vector += Half;
            vector = Vector4.Clamp(vector, Vector4.Zero, MaxBytes);

            bytes[startIndex] = (byte)vector.X;
            bytes[startIndex + 1] = (byte)vector.Y;
            bytes[startIndex + 2] = (byte)vector.Z;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToXyzwBytes(byte[] bytes, int startIndex)
        {
            Vector4 vector = this.ToVector4();
            vector *= MaxBytes;
            vector += Half;
            vector = Vector4.Clamp(vector, Vector4.Zero, MaxBytes);

            bytes[startIndex] = (byte)vector.X;
            bytes[startIndex + 1] = (byte)vector.Y;
            bytes[startIndex + 2] = (byte)vector.Z;
            bytes[startIndex + 3] = (byte)vector.W;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToZyxBytes(byte[] bytes, int startIndex)
        {
            Vector4 vector = this.ToVector4();
            vector *= MaxBytes;
            vector += Half;
            vector = Vector4.Clamp(vector, Vector4.Zero, MaxBytes);

            bytes[startIndex] = (byte)vector.Z;
            bytes[startIndex + 1] = (byte)vector.Y;
            bytes[startIndex + 2] = (byte)vector.X;
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ToZyxwBytes(byte[] bytes, int startIndex)
        {
            Vector4 vector = this.ToVector4();
            vector *= MaxBytes;
            vector += Half;
            vector = Vector4.Clamp(vector, Vector4.Zero, MaxBytes);

            bytes[startIndex] = (byte)vector.Z;
            bytes[startIndex + 1] = (byte)vector.Y;
            bytes[startIndex + 2] = (byte)vector.X;
            bytes[startIndex + 3] = (byte)vector.W;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return (obj is HalfSingle) && this.Equals((HalfSingle)obj);
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(HalfSingle other)
        {
            return this.PackedValue == other.PackedValue;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this.ToSingle().ToString();
        }

        /// <inheritdoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode()
        {
            return this.PackedValue.GetHashCode();
        }
    }
}