﻿// <copyright file="Rgba32.ColorspaceTransforms.cs" company="James Jackson-South">
// Copyright (c) James Jackson-South and contributors.
// Licensed under the Apache License, Version 2.0.
// </copyright>

namespace ImageSharp
{
    using System;
    using System.Numerics;
    using Colors.Spaces;

    /// <content>
    /// Provides implicit colorspace transformation.
    /// </content>
    public partial struct Rgba32
    {
        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="Rgba32"/> to a
        /// <see cref="Bgra32"/>.
        /// </summary>
        /// <param name="color">The instance of <see cref="Rgba32"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Bgra32"/>.
        /// </returns>
        public static implicit operator Rgba32(Bgra32 color)
        {
            return new Rgba32(color.R, color.G, color.B, color.A);
        }

        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="Cmyk"/> to a
        /// <see cref="Rgba32"/>.
        /// </summary>
        /// <param name="cmykColor">The instance of <see cref="Cmyk"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Rgba32"/>.
        /// </returns>
        public static implicit operator Rgba32(Cmyk cmykColor)
        {
            float r = (1 - cmykColor.C) * (1 - cmykColor.K);
            float g = (1 - cmykColor.M) * (1 - cmykColor.K);
            float b = (1 - cmykColor.Y) * (1 - cmykColor.K);
            return new Rgba32(r, g, b, 1);
        }

        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="YCbCr"/> to a
        /// <see cref="Rgba32"/>.
        /// </summary>
        /// <param name="color">The instance of <see cref="YCbCr"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Rgba32"/>.
        /// </returns>
        public static implicit operator Rgba32(YCbCr color)
        {
            float y = color.Y;
            float cb = color.Cb - 128;
            float cr = color.Cr - 128;

            byte r = (byte)(y + (1.402F * cr)).Clamp(0, 255);
            byte g = (byte)(y - (0.34414F * cb) - (0.71414F * cr)).Clamp(0, 255);
            byte b = (byte)(y + (1.772F * cb)).Clamp(0, 255);

            return new Rgba32(r, g, b);
        }

        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="CieXyz"/> to a
        /// <see cref="Rgba32"/>.
        /// </summary>
        /// <param name="color">The instance of <see cref="CieXyz"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Rgba32"/>.
        /// </returns>
        public static implicit operator Rgba32(CieXyz color)
        {
            float x = color.X / 100F;
            float y = color.Y / 100F;
            float z = color.Z / 100F;

            // Then XYZ to RGB (multiplication by 100 was done above already)
            float r = (x * 3.2406F) + (y * -1.5372F) + (z * -0.4986F);
            float g = (x * -0.9689F) + (y * 1.8758F) + (z * 0.0415F);
            float b = (x * 0.0557F) + (y * -0.2040F) + (z * 1.0570F);

            Vector4 vector = new Vector4(r, g, b, 1).Compress();
            return new Rgba32(vector);
        }

        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="Hsv"/> to a
        /// <see cref="Rgba32"/>.
        /// </summary>
        /// <param name="color">The instance of <see cref="Hsv"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Rgba32"/>.
        /// </returns>
        public static implicit operator Rgba32(Hsv color)
        {
            float s = color.S;
            float v = color.V;

            if (MathF.Abs(s) < Constants.Epsilon)
            {
                return new Rgba32(v, v, v, 1);
            }

            float h = (MathF.Abs(color.H - 360) < Constants.Epsilon) ? 0 : color.H / 60;
            int i = (int)Math.Truncate(h);
            float f = h - i;

            float p = v * (1.0F - s);
            float q = v * (1.0F - (s * f));
            float t = v * (1.0F - (s * (1.0F - f)));

            float r, g, b;
            switch (i)
            {
                case 0:
                    r = v;
                    g = t;
                    b = p;
                    break;

                case 1:
                    r = q;
                    g = v;
                    b = p;
                    break;

                case 2:
                    r = p;
                    g = v;
                    b = t;
                    break;

                case 3:
                    r = p;
                    g = q;
                    b = v;
                    break;

                case 4:
                    r = t;
                    g = p;
                    b = v;
                    break;

                default:
                    r = v;
                    g = p;
                    b = q;
                    break;
            }

            return new Rgba32(r, g, b, 1);
        }

        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="Hsl"/> to a
        /// <see cref="Rgba32"/>.
        /// </summary>
        /// <param name="color">The instance of <see cref="Hsl"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Rgba32"/>.
        /// </returns>
        public static implicit operator Rgba32(Hsl color)
        {
            float rangedH = color.H / 360F;
            float r = 0;
            float g = 0;
            float b = 0;
            float s = color.S;
            float l = color.L;

            if (MathF.Abs(l) > Constants.Epsilon)
            {
                if (MathF.Abs(s) < Constants.Epsilon)
                {
                    r = g = b = l;
                }
                else
                {
                    float temp2 = (l < 0.5f) ? l * (1f + s) : l + s - (l * s);
                    float temp1 = (2f * l) - temp2;

                    r = GetColorComponent(temp1, temp2, rangedH + 0.3333333F);
                    g = GetColorComponent(temp1, temp2, rangedH);
                    b = GetColorComponent(temp1, temp2, rangedH - 0.3333333F);
                }
            }

            return new Rgba32(r, g, b, 1);
        }

        /// <summary>
        /// Allows the implicit conversion of an instance of <see cref="CieLab"/> to a
        /// <see cref="Rgba32"/>.
        /// </summary>
        /// <param name="cieLabColor">The instance of <see cref="CieLab"/> to convert.</param>
        /// <returns>
        /// An instance of <see cref="Rgba32"/>.
        /// </returns>
        public static implicit operator Rgba32(CieLab cieLabColor)
        {
            // First convert back to XYZ...
            float y = (cieLabColor.L + 16F) / 116F;
            float x = (cieLabColor.A / 500F) + y;
            float z = y - (cieLabColor.B / 200F);

            float x3 = x * x * x;
            float y3 = y * y * y;
            float z3 = z * z * z;

            x = x3 > 0.008856F ? x3 : (x - 0.137931F) / 7.787F;
            y = (cieLabColor.L > 7.999625F) ? y3 : (cieLabColor.L / 903.3F);
            z = (z3 > 0.008856F) ? z3 : (z - 0.137931F) / 7.787F;

            x *= 0.95047F;
            z *= 1.08883F;

            // Then XYZ to RGB (multiplication by 100 was done above already)
            float r = (x * 3.2406F) + (y * -1.5372F) + (z * -0.4986F);
            float g = (x * -0.9689F) + (y * 1.8758F) + (z * 0.0415F);
            float b = (x * 0.0557F) + (y * -0.2040F) + (z * 1.0570F);

            return new Rgba32(new Vector4(r, g, b, 1F).Compress());
        }

        /// <summary>
        /// Gets the color component from the given values.
        /// </summary>
        /// <param name="first">The first value.</param>
        /// <param name="second">The second value.</param>
        /// <param name="third">The third value.</param>
        /// <returns>
        /// The <see cref="float"/>.
        /// </returns>
        private static float GetColorComponent(float first, float second, float third)
        {
            third = MoveIntoRange(third);
            if (third < 0.1666667F)
            {
                return first + ((second - first) * 6.0f * third);
            }

            if (third < 0.5)
            {
                return second;
            }

            if (third < 0.6666667F)
            {
                return first + ((second - first) * (0.6666667F - third) * 6.0f);
            }

            return first;
        }

        /// <summary>
        /// Moves the specific value within the acceptable range for
        /// conversion.
        /// <remarks>Used for converting <see cref="Hsl"/> colors to this type.</remarks>
        /// </summary>
        /// <param name="value">The value to shift.</param>
        /// <returns>
        /// The <see cref="float"/>.
        /// </returns>
        private static float MoveIntoRange(float value)
        {
            if (value < 0.0)
            {
                value += 1.0f;
            }
            else if (value > 1.0)
            {
                value -= 1.0f;
            }

            return value;
        }
    }
}
