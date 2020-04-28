// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using static System.Runtime.InteropServices.MemoryMarshal;

namespace System.Numerics
{
    internal static partial class BigIntegerCalculator
    {
        public static uint[] Divide(uint[] left, uint right,
                                    out uint remainder)
        {
            Debug.Assert(left != null);
            Debug.Assert(left.Length >= 1);

            // Executes the division for one big and one 32-bit integer.
            // Thus, we've similar code than below, but there is no loop for
            // processing the 32-bit integer, since it's a single element.

            uint[] quotient = new uint[left.Length];

            Divide(left, right, ref GetArrayDataReference(quotient), out remainder);

            return quotient;
        }

        private static void Divide(ReadOnlySpan<uint> left,
                                        uint right,
                                        ref uint quotient,
                                        out uint remainder)
        {
            ulong carry = 0UL;
            for (int i = left.Length - 1; i >= 0; i--)
            {
                ulong value = (carry << 32) | Unsafe.Add(ref GetReference(left), i);
                ulong digit = value / right;
                Unsafe.Add(ref quotient, i) = (uint)digit;
                carry = value - digit * right;
            }
            remainder = (uint)carry;
        }

        public static uint[] Divide(uint[] left, uint right)
        {
            Debug.Assert(left != null);
            Debug.Assert(left.Length >= 1);

            // Same as above, but only computing the quotient.

            uint[] quotient = new uint[left.Length];

            Divide(left, right, ref GetArrayDataReference(quotient));

            return quotient;
        }

        private static void Divide(ReadOnlySpan<uint> left, uint right, ref uint quotient)
        {
            ulong carry = 0UL;
            for (int i = left.Length - 1; i >= 0; i--)
            {
                ulong value = (carry << 32) | Unsafe.Add(ref GetReference(left), i);
                ulong digit = value / right;
                Unsafe.Add(ref quotient, i) = (uint)digit;
                carry = value - digit * right;
            }
        }

        public static uint Remainder(ReadOnlySpan<uint> left, uint right)
        {
            Debug.Assert(left.Length >= 1);

            // Same as above, but only computing the remainder.
            ulong carry = 0UL;
            for (int i = left.Length - 1; i >= 0; i--)
            {
                ulong value = (carry << 32) | Unsafe.Add(ref GetReference(left), i);
                carry = value % right;
            }

            return (uint)carry;
        }

        public static uint[] Divide(uint[] left, uint[] right,
                                           out uint[] remainder)
        {
            Debug.Assert(left != null);
            Debug.Assert(right != null);
            Debug.Assert(left.Length >= 1);
            Debug.Assert(right.Length >= 1);
            Debug.Assert(left.Length >= right.Length);

            // Switching to managed pointers helps sparing
            // some nasty index calculations...

            // NOTE: left will get overwritten, we need a local copy

            uint[] localLeft = CreateCopy(left);
            uint[] bits = new uint[left.Length - right.Length + 1];

            Divide(localLeft, right, bits);

            remainder = localLeft;

            return bits;
        }

        public static uint[] Divide(uint[] left, uint[] right)
        {
            Debug.Assert(left != null);
            Debug.Assert(right != null);
            Debug.Assert(left.Length >= 1);
            Debug.Assert(right.Length >= 1);
            Debug.Assert(left.Length >= right.Length);

            // Same as above, but only returning the quotient.

            uint[] bits = new uint[left.Length - right.Length + 1];

            // NOTE: left will get overwritten, we need a local copy

            Divide(CreateCopy(left), right, bits);

            return bits;
        }

        public static uint[] Remainder(uint[] left, uint[] right)
        {
            Debug.Assert(left != null);
            Debug.Assert(right != null);
            Debug.Assert(left.Length >= 1);
            Debug.Assert(right.Length >= 1);
            Debug.Assert(left.Length >= right.Length);

            // Same as above, but only returning the remainder.

            // NOTE: left will get overwritten, we need a local copy

            uint[] localLeft = CreateCopy(left);

            Divide(localLeft, right, default);

            return localLeft;
        }

        private static void Divide(Span<uint> left, ReadOnlySpan<uint> right, Span<uint> bits)
        {
            Debug.Assert(left.Length >= 1);
            Debug.Assert(right.Length >= 1);
            Debug.Assert(left.Length >= right.Length);
            Debug.Assert(bits.Length == left.Length - right.Length + 1
                || bits.Length == 0);

            // Executes the "grammar-school" algorithm for computing q = a / b.
            // Before calculating q_i, we get more bits into the highest bit
            // block of the divisor. Thus, guessing digits of the quotient
            // will be more precise. Additionally we'll get r = a % b.

            uint divHi = Unsafe.Add(ref GetReference(right), right.Length - 1);
            uint divLo = right.Length > 1 ? Unsafe.Add(ref GetReference(right), right.Length - 2) : 0;

            // We measure the leading zeros of the divisor
            int shift = LeadingZeros(divHi);
            int backShift = 32 - shift;

            // And, we make sure the most significant bit is set
            if (shift > 0)
            {
                uint divNx = right.Length > 2 ? Unsafe.Add(ref GetReference(right), right.Length - 3) : 0;

                divHi = (divHi << shift) | (divLo >> backShift);
                divLo = (divLo << shift) | (divNx >> backShift);
            }

            // Then, we divide all of the bits as we would do it using
            // pen and paper: guessing the next digit, subtracting, ...
            for (int i = left.Length; i >= right.Length; i--)
            {
                int n = i - right.Length;
                uint t = i < left.Length ? Unsafe.Add(ref GetReference(left), i) : 0;

                ulong valHi = ((ulong)t << 32) | Unsafe.Add(ref GetReference(left), i - 1);
                uint valLo = i > 1 ? Unsafe.Add(ref GetReference(left), i - 2) : 0;

                // We shifted the divisor, we shift the dividend too
                if (shift > 0)
                {
                    uint valNx = i > 2 ? Unsafe.Add(ref GetReference(left), i - 3) : 0;

                    valHi = (valHi << shift) | (valLo >> backShift);
                    valLo = (valLo << shift) | (valNx >> backShift);
                }

                // First guess for the current digit of the quotient,
                // which naturally must have only 32 bits...
                ulong digit = valHi / divHi;
                if (digit > 0xFFFFFFFF)
                    digit = 0xFFFFFFFF;

                // Our first guess may be a little bit to big
                while (DivideGuessTooBig(digit, valHi, valLo, divHi, divLo))
                    --digit;

                if (digit > 0)
                {
                    // Now it's time to subtract our current quotient
                    uint carry = SubtractDivisor(left.Slice(n), right, digit);
                    if (carry != t)
                    {
                        Debug.Assert(carry == t + 1);

                        // Our guess was still exactly one too high
                        carry = AddDivisor(left.Slice(n), right);
                        --digit;

                        Debug.Assert(carry == 1);
                    }
                }

                // We have the digit!
                if (!bits.IsEmpty)
                    Unsafe.Add(ref GetReference(bits), n) = (uint)digit;
                if (i < left.Length)
                    Unsafe.Add(ref GetReference(left), i) = 0;
            }
        }

        private static uint AddDivisor(Span<uint> left, ReadOnlySpan<uint> right)
        {
            Debug.Assert(left.Length >= 0);
            Debug.Assert(right.Length >= 0);
            Debug.Assert(left.Length >= right.Length);

            // Repairs the dividend, if the last subtract was too much

            ulong carry = 0UL;

            for (int i = 0; i < right.Length; i++)
            {
                ref uint leftElement = ref Unsafe.Add(ref GetReference(left), i);
                ulong digit = (leftElement + carry) + Unsafe.Add(ref GetReference(right), i);
                leftElement = unchecked((uint)digit);
                carry = digit >> 32;
            }

            return (uint)carry;
        }

        private static uint SubtractDivisor(Span<uint> left, ReadOnlySpan<uint> right, ulong q)
        {
            Debug.Assert(left.Length >= 0);
            Debug.Assert(right.Length >= 0);
            Debug.Assert(left.Length >= right.Length);
            Debug.Assert(q <= 0xFFFFFFFF);

            // Combines a subtract and a multiply operation, which is naturally
            // more efficient than multiplying and then subtracting...

            ulong carry = 0UL;

            for (int i = 0; i < right.Length; i++)
            {
                carry += Unsafe.Add(ref GetReference(right), i) * q;
                uint digit = unchecked((uint)carry);
                carry = carry >> 32;
                ref uint leftElement = ref Unsafe.Add(ref GetReference(left), i);
                if (leftElement < digit)
                    ++carry;
                leftElement = unchecked(leftElement - digit);
            }

            return (uint)carry;
        }

        private static bool DivideGuessTooBig(ulong q, ulong valHi, uint valLo,
                                              uint divHi, uint divLo)
        {
            Debug.Assert(q <= 0xFFFFFFFF);

            // We multiply the two most significant limbs of the divisor
            // with the current guess for the quotient. If those are bigger
            // than the three most significant limbs of the current dividend
            // we return true, which means the current guess is still too big.

            ulong chkHi = divHi * q;
            ulong chkLo = divLo * q;

            chkHi = chkHi + (chkLo >> 32);
            chkLo = chkLo & 0xFFFFFFFF;

            if (chkHi < valHi)
                return false;
            if (chkHi > valHi)
                return true;

            if (chkLo < valLo)
                return false;
            if (chkLo > valLo)
                return true;

            return false;
        }

        private static uint[] CreateCopy(uint[] value)
        {
            Debug.Assert(value != null);
            Debug.Assert(value.Length != 0);

            uint[] bits = new uint[value.Length];
            Array.Copy(value, bits, bits.Length);
            return bits;
        }

        private static int LeadingZeros(uint value)
        {
            if (value == 0)
                return 32;

            int count = 0;
            if ((value & 0xFFFF0000) == 0)
            {
                count += 16;
                value = value << 16;
            }
            if ((value & 0xFF000000) == 0)
            {
                count += 8;
                value = value << 8;
            }
            if ((value & 0xF0000000) == 0)
            {
                count += 4;
                value = value << 4;
            }
            if ((value & 0xC0000000) == 0)
            {
                count += 2;
                value = value << 2;
            }
            if ((value & 0x80000000) == 0)
            {
                count += 1;
            }

            return count;
        }
    }
}
