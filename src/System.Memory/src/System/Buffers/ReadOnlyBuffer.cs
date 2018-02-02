﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Buffers
{
    /// <summary>
    /// Represents a buffer that can read a sequential series of <typeparam name="T">T</typeparam>.
    /// </summary>
    public readonly partial struct ReadOnlyBuffer<T>
    {
        private const int IndexBitMask = 0x7FFFFFFF;

        private const int MemoryListStartMask = 0;
        private const int MemoryListEndMask = 0;

        private const int ArrayStartMask = 0;
        private const int ArrayEndMask = 1 << 31;

        private const int OwnedMemoryStartMask = 1 << 31;
        private const int OwnedMemoryEndMask = 0;

        private readonly SequencePosition _bufferStart;
        private readonly SequencePosition _bufferEnd;


        /// <summary>
        /// Returns empty <see cref="ReadOnlyBuffer{T}"/>
        /// </summary>
        public static readonly ReadOnlyBuffer<T> Empty = new ReadOnlyBuffer<T>(new T[0]);

        /// <summary>
        /// Length of the <see cref="ReadOnlyBuffer{T}"/>.
        /// </summary>
        public long Length => GetLength(_bufferStart, _bufferEnd);

        /// <summary>
        /// Determines if the <see cref="ReadOnlyBuffer{T}"/> is empty.
        /// </summary>
        public bool IsEmpty => Length == 0;

        /// <summary>
        /// Determins if the <see cref="ReadOnlyBuffer{T}"/> is a single <see cref="ReadOnlyMemory{T}"/>.
        /// </summary>
        public bool IsSingleSegment => _bufferStart.Segment == _bufferEnd.Segment;

        /// <summary>
        /// Gets <see cref="ReadOnlyMemory{T}"/> from the first segment.
        /// </summary>
        public ReadOnlyMemory<T> First
        {
            get
            {
                TryGetBuffer(_bufferStart, _bufferEnd, out ReadOnlyMemory<T> first, out _);
                return first;
            }
        }

        /// <summary>
        /// A position to the start of the <see cref="ReadOnlyBuffer{T}"/>.
        /// </summary>
        public SequencePosition Start => _bufferStart;

        /// <summary>
        /// A position to the end of the <see cref="ReadOnlyBuffer{T}"/>
        /// </summary>
        public SequencePosition End => _bufferEnd;

        private ReadOnlyBuffer(object startSegment, int startIndex, object endSegment, int endIndex)
        {
            Debug.Assert(startSegment != null);
            Debug.Assert(endSegment != null);

            _bufferStart = new SequencePosition(startSegment, startIndex);
            _bufferEnd = new SequencePosition(endSegment, endIndex);
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlyBuffer{T}"/> from linked memory list represented by start and end segments
        /// and coresponding indexes in them.
        /// </summary>
        public ReadOnlyBuffer(IMemoryList<T> startSegment, int startIndex, IMemoryList<T> endSegment, int endIndex)
        {
            Debug.Assert(startSegment != null);
            Debug.Assert(endSegment != null);
            Debug.Assert(startSegment.Memory.Length >= startIndex);
            Debug.Assert(endSegment.Memory.Length >= endIndex);

            _bufferStart = new SequencePosition(startSegment, startIndex | MemoryListStartMask);
            _bufferEnd = new SequencePosition(endSegment, endIndex | MemoryListEndMask);
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlyBuffer{T}"/> from the <see cref="T:T[]"/>.
        /// </summary>
        public ReadOnlyBuffer(T[] array) : this(array, 0, array.Length)
        {
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlyBuffer{T}"/> from the <see cref="T:T[]"/> offset and index.
        /// </summary>
        public ReadOnlyBuffer(T[] array, int offset, int length)
        {
            if (array == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.array);
            }

            _bufferStart = new SequencePosition(array, offset | ArrayStartMask);
            _bufferEnd = new SequencePosition(array, offset + length | ArrayEndMask);
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlyBuffer{T}"/> from the <see cref="ReadOnlyMemory{T}"/>.
        /// Consumer is expected to manage lifetime of memory until  <see cref="ReadOnlyBuffer{T}"/> is not used anymore.
        /// </summary>
        public ReadOnlyBuffer(ReadOnlyMemory<T> readOnlyMemory)
        {
            ReadOnlyBufferSegment segment = new ReadOnlyBufferSegment
            {
                Memory = MemoryMarshal.AsMemory(readOnlyMemory)
            };
            _bufferStart = new SequencePosition(segment, 0 | MemoryListStartMask);
            _bufferEnd = new SequencePosition(segment, readOnlyMemory.Length | MemoryListEndMask);
        }
        /// <summary>
        /// Creates an instance of <see cref="ReadOnlyBuffer{T}"/> from the <see cref="OwnedMemory{T}"/>.
        /// Consumer is expected to manage lifetime of memory until  <see cref="ReadOnlyBuffer{T}"/> is not used anymore.
        /// </summary>
        public ReadOnlyBuffer(OwnedMemory<T> ownedMemory): this(ownedMemory, 0, ownedMemory.Length)
        {
        }

        /// <summary>
        /// Creates an instance of <see cref="ReadOnlyBuffer{T}"/> from the <see cref="OwnedMemory{T}"/>.
        /// Consumer is expected to manage lifetime of memory until  <see cref="ReadOnlyBuffer{T}"/> is not used anymore.
        /// </summary>
        public ReadOnlyBuffer(OwnedMemory<T> ownedMemory, int offset, int length)
        {
            if (ownedMemory == null)
            {
                ThrowHelper.ThrowArgumentNullException(ExceptionArgument.ownedMemory);
            }

            _bufferStart = new SequencePosition(ownedMemory, offset | OwnedMemoryStartMask);
            _bufferEnd = new SequencePosition(ownedMemory, length | OwnedMemoryEndMask);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, and is at most <paramref name="length"/> items
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The length of the slice</param>
        public ReadOnlyBuffer<T> Slice(long start, long length)
        {
            SequencePosition begin = Seek(_bufferStart, _bufferEnd, start, false);
            SequencePosition end = Seek(begin, _bufferEnd, length, false);
            return SliceImpl(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, ending at <paramref name="end"/> (inclusive).
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="end">The end (inclusive) of the slice</param>
        public ReadOnlyBuffer<T> Slice(long start, SequencePosition end)
        {
            BoundsCheck(_bufferEnd, end);

            SequencePosition begin = Seek(_bufferStart, end, start);
            return SliceImpl(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, and is at most <paramref name="length"/> items
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <param name="length">The length of the slice</param>
        public ReadOnlyBuffer<T> Slice(SequencePosition start, long length)
        {
            BoundsCheck(_bufferEnd, start);

            SequencePosition end = Seek(start, _bufferEnd, length, false);
            return SliceImpl(start, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, and is at most <paramref name="length"/> items
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="length">The length of the slice</param>
        public ReadOnlyBuffer<T> Slice(int start, int length)
        {
            SequencePosition begin = Seek(_bufferStart, _bufferEnd, start, false);
            SequencePosition end = Seek(begin, _bufferEnd, length, false);
            return SliceImpl(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, ending at <paramref name="end"/> (inclusive).
        /// </summary>
        /// <param name="start">The index at which to begin this slice.</param>
        /// <param name="end">The end (inclusive) of the slice</param>
        public ReadOnlyBuffer<T> Slice(int start, SequencePosition end)
        {
            BoundsCheck(_bufferEnd, end);

            SequencePosition begin = Seek(_bufferStart, end, start);
            return SliceImpl(begin, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at '<paramref name="start"/>, and is at most <paramref name="length"/> items
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <param name="length">The length of the slice</param>
        public ReadOnlyBuffer<T> Slice(SequencePosition start, int length)
        {
            BoundsCheck(_bufferEnd, start);

            SequencePosition end = Seek(start, _bufferEnd, length, false);
            return SliceImpl(start, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, ending at <paramref name="end"/> (inclusive).
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        /// <param name="end">The ending (inclusive) <see cref="SequencePosition"/> of the slice</param>
        public ReadOnlyBuffer<T> Slice(SequencePosition start, SequencePosition end)
        {
            BoundsCheck(_bufferEnd, end);
            BoundsCheck(end, start);

            return SliceImpl(start, end);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, ending at the existing <see cref="ReadOnlyBuffer{T}"/>'s end.
        /// </summary>
        /// <param name="start">The starting (inclusive) <see cref="SequencePosition"/> at which to begin this slice.</param>
        public ReadOnlyBuffer<T> Slice(SequencePosition start)
        {
            BoundsCheck(_bufferEnd, start);

            return SliceImpl(start, _bufferEnd);
        }

        /// <summary>
        /// Forms a slice out of the given <see cref="ReadOnlyBuffer{T}"/>, beginning at <paramref name="start"/>, ending at the existing <see cref="ReadOnlyBuffer{T}"/>'s end.
        /// </summary>
        /// <param name="start">The start index at which to begin this slice.</param>
        public ReadOnlyBuffer<T> Slice(long start)
        {
            if (start == 0) return this;

            SequencePosition begin = Seek(_bufferStart, _bufferEnd, start, false);
            return SliceImpl(begin, _bufferEnd);
        }

        /// <inheritdoc />
        public override string ToString() => string.Format("System.Buffers.ReadOnlyBuffer<{0}>[{1}]", typeof(T).Name, Length);

        /// <summary>
        /// Returns an enumerator over the <see cref="ReadOnlyBuffer{T}"/>
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        /// <summary>
        /// Returns new <see cref="SequencePosition"/> that is offset by <paramref name="offset"/> starting with <paramref name="origin"/>
        /// </summary>
        public SequencePosition GetPosition(SequencePosition origin, long offset)
        {
            if (offset < 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(ExceptionArgument.offset);
            }
            return Seek(origin, _bufferEnd, offset, false);
        }

        /// <summary>
        /// Tries to retrieve next segment after <paramref name="position"/> and return it's contents in <paramref name="data"/>.
        /// Returns <code>false</code> if end of <see cref="ReadOnlyBuffer{T}"/> was reached otherwise <code>false</code>.
        /// Sets <paramref name="position"/> to the beginning of next segment is <paramref name="advance"/> is set to <code>true</code>.
        /// </summary>
        public bool TryGet(ref SequencePosition position, out ReadOnlyMemory<T> data, bool advance = true)
        {
            bool result = TryGetBuffer(position, End, out data, out SequencePosition next);
            if (advance)
            {
                position = next;
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlyBuffer<T> SliceImpl(SequencePosition begin, SequencePosition end)
        {
            // In this methods we reset high order bits from indices
            // of positions that were passed in
            // and apply type bits specific for current ReadOnlyBuffer type

            return new ReadOnlyBuffer<T>(
                begin.Segment,
                begin.Index & IndexBitMask | (Start.Index & ~IndexBitMask),
                end.Segment,
                end.Index & IndexBitMask | (End.Index & ~IndexBitMask)
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private BufferType GetBufferType()
        {
            // We take high order bits of two indexes index and move them
            // to a first and second position to convert to BufferType
            // Masking with 2 is required to only keep the second bit of Start.Index
            return (BufferType)((((uint)Start.Index >> 30) & 2) | (uint)End.Index >> 31);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetIndex(int index)
        {
            return index & IndexBitMask;
        }

        private enum BufferType
        {
            MemoryList = 0x00,
            Array = 0x1,
            OwnedMemory = 0x2
        }

        /// <summary>
        /// An enumerator over the <see cref="ReadOnlyBuffer{T}"/>
        /// </summary>
        public struct Enumerator
        {
            private readonly ReadOnlyBuffer<T> _readOnlyBuffer;
            private SequencePosition _next;
            private ReadOnlyMemory<T> _currentMemory;

            /// <summary>Initialize the enumerator.</summary>
            /// <param name="readOnlyBuffer">The <see cref="ReadOnlyBuffer{T}"/> to enumerate.</param>
            public Enumerator(ReadOnlyBuffer<T> readOnlyBuffer)
            {
                _readOnlyBuffer = readOnlyBuffer;
                _currentMemory = default;
                _next = readOnlyBuffer.Start;
            }

            /// <summary>
            /// The current <see cref="ReadOnlyMemory{T}"/>
            /// </summary>
            public ReadOnlyMemory<T> Current => _currentMemory;

            /// <summary>
            /// Moves to the next <see cref="ReadOnlyMemory{T}"/> in the <see cref="ReadOnlyBuffer{T}"/>
            /// </summary>
            /// <returns></returns>
            public bool MoveNext()
            {
                if (_next.Segment == null)
                {
                    return false;
                }

                return _readOnlyBuffer.TryGet(ref _next, out _currentMemory);
            }
        }
    }
}
