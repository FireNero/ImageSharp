// <copyright file="BufferSpan.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace ImageSharp
{
    using System;
    using System.Numerics;
    using System.Runtime.CompilerServices;
    using System.Runtime.InteropServices;

    /// <summary>
    /// Utility methods for <see cref="BufferSpan{T}"/>
    /// </summary>
    internal static class BufferSpan
    {
        /// <summary>
        /// Fetches a <see cref="Vector{T}"/> from the beginning of the span.
        /// </summary>
        /// <typeparam name="T">The value type</typeparam>
        /// <param name="span">The span to fetch the vector from</param>
        /// <returns>A <see cref="Vector{T}"/> reference to the beginning of the span</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref Vector<T> FetchVector<T>(this BufferSpan<T> span)
            where T : struct
        {
            return ref Unsafe.As<T, Vector<T>>(ref span.DangerousGetPinnableReference());
        }

        /// <summary>
        /// Copy 'count' number of elements of the same type from 'source' to 'dest'
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The <see cref="BufferSpan{T}"/> to copy elements from.</param>
        /// <param name="destination">The destination <see cref="BufferSpan{T}"/>.</param>
        /// <param name="count">The number of elements to copy</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void Copy<T>(BufferSpan<T> source, BufferSpan<T> destination, int count)
            where T : struct
        {
            DebugGuard.MustBeLessThanOrEqualTo(count, source.Length, nameof(count));
            DebugGuard.MustBeLessThanOrEqualTo(count, destination.Length, nameof(count));

            ref byte srcRef = ref Unsafe.As<T, byte>(ref source.DangerousGetPinnableReference());
            ref byte destRef = ref Unsafe.As<T, byte>(ref destination.DangerousGetPinnableReference());

            int byteCount = Unsafe.SizeOf<T>() * count;

            // TODO: Use unfixed Unsafe.CopyBlock(ref T, ref T, int) for small blocks, when it gets available!
            fixed (byte* pSrc = &srcRef)
            fixed (byte* pDest = &destRef)
            {
#if NETSTANDARD1_1
                Unsafe.CopyBlock(pDest, pSrc, (uint)byteCount);
#else
                int destLength = destination.Length * Unsafe.SizeOf<T>();
                Buffer.MemoryCopy(pSrc, pDest, destLength, byteCount);
#endif
            }
        }

        /// <summary>
        /// Copy all elements of 'source' into 'destination'.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="source">The <see cref="BufferSpan{T}"/> to copy elements from.</param>
        /// <param name="destination">The destination <see cref="BufferSpan{T}"/>.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Copy<T>(BufferSpan<T> source, BufferSpan<T> destination)
            where T : struct
        {
            Copy(source, destination, source.Length);
        }

        /// <summary>
        /// Gets the size of `count` elements in bytes.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="count">The count of the elements</param>
        /// <returns>The size in bytes as int</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>(int count)
            where T : struct => Unsafe.SizeOf<T>() * count;

        /// <summary>
        /// Gets the size of `count` elements in bytes as UInt32
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="count">The count of the elements</param>
        /// <returns>The size in bytes as UInt32</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint USizeOf<T>(int count)
            where T : struct
            => (uint)SizeOf<T>(count);
    }
}