﻿// -----------------------------------------------------------------------
//  <copyright file="Iterators.cs" company="Hibernating Rhinos LTD">
//      Copyright (c) Hibernating Rhinos LTD. All rights reserved.
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Voron.Impl.FileHeaders;

namespace Voron.Trees.Fixed
{
    public unsafe partial class FixedSizeTree
    {
        public interface IFixedSizeIterator : IDisposable
        {
            bool Seek(long key);
            long CurrentKey { get; }
            Slice Value { get; }
            bool MoveNext();
            ValueReader CreateReaderForCurrent();
        }

        public class NullIterator : IFixedSizeIterator
        {
            public bool Seek(long key)
            {
                return false;
            }

            public long CurrentKey { get { throw new InvalidOperationException("Invalid position, cannot read past end of tree"); } }
            public Slice Value { get { throw new InvalidOperationException("Invalid position, cannot read past end of tree"); } }
            public bool MoveNext()
            {
                return false;
            }

            public void Dispose()
            {
            }

            public ValueReader CreateReaderForCurrent()
            {
                throw new InvalidOperationException("No current page");
            }
        }

        public class EmbeddedIterator : IFixedSizeIterator
        {
            private readonly FixedSizeTree _fst;
            private int _pos;
            private readonly FixedSizeTreeHeader.Embedded* _header;
            private readonly byte* _dataStart;

            public EmbeddedIterator(FixedSizeTree fst)
            {
                _fst = fst;
                var ptr = _fst._parent.DirectRead(_fst._treeName);
                _header = (FixedSizeTreeHeader.Embedded*)ptr;
                _dataStart = ptr + sizeof(FixedSizeTreeHeader.Embedded);
            }

            public bool Seek(long key)
            {
                _pos = _fst.BinarySearch(_dataStart, _header->NumberOfEntries, key, _fst._entrySize);
                return _pos != _header->NumberOfEntries;
            }

            public long CurrentKey
            {
                get
                {
                    if (_pos == _header->NumberOfEntries)
                        throw new InvalidOperationException("Invalid position, cannot read past end of tree");
                    return _fst.KeyFor(_dataStart, _pos, _fst._entrySize);
                }
            }

            public Slice Value
            {
                get
                {
                    if (_pos == _header->NumberOfEntries)
                        throw new InvalidOperationException("Invalid position, cannot read past end of tree");

                    return new Slice(_dataStart + (_pos * _fst._entrySize) + sizeof(long), _fst._valSize);
                }
            }

            public bool MoveNext()
            {
                return ++_pos < _header->NumberOfEntries;
            }

            public ValueReader CreateReaderForCurrent()
            {
                return new ValueReader(_dataStart + (_pos * _fst._entrySize) + sizeof(long), _fst._valSize);
            }

            public void Dispose()
            {
            }
        }

        public class LargeIterator : IFixedSizeIterator
        {
            private readonly FixedSizeTree _parent;
            private Page _currentPage;

            public LargeIterator(FixedSizeTree parent)
            {
                _parent = parent;
            }

            public void Dispose()
            {
                
            }

            public bool Seek(long key)
            {
                _currentPage = _parent.FindPageFor(key);
                var seek = _currentPage.LastSearchPosition != _currentPage.FixedSize_NumberOfEntries;
                if (seek == false)
                    _currentPage = null;
                return seek;
            }

            public long CurrentKey
            {
                get
                {
                    if (_currentPage == null)
                        throw new InvalidOperationException("No current page was set");

                    return _parent.KeyFor(_currentPage.Base + _currentPage.FixedSize_StartPosition,
                        _currentPage.LastSearchPosition, _parent._entrySize);
                }
            }

            public Slice Value
            {
                get
                {
                    if (_currentPage == null)
                        throw new InvalidOperationException("No current page was set");

                    return new Slice(_currentPage.Base + _currentPage.FixedSize_StartPosition + (_parent._entrySize * _currentPage.LastSearchPosition) + sizeof(long), _parent._valSize);
                }
            }

            public bool MoveNext()
            {
                if (_currentPage == null)
                    throw new InvalidOperationException("No current page was set");

                while (_currentPage != null)
                {
                    _currentPage.LastSearchPosition++;
                    if (_currentPage.LastSearchPosition < _currentPage.FixedSize_NumberOfEntries)
                    {
                        // run out of entries, need to select the next page...
                        while (_currentPage.IsBranch)
                        {
                            _parent._cursor.Push(_currentPage);
                            var childParentNumber = _parent.PageValueFor(_currentPage.Base + _currentPage.FixedSize_StartPosition,_currentPage.LastSearchPosition);
                            _currentPage = _parent._tx.GetReadOnlyPage(childParentNumber);

                            _currentPage.LastSearchPosition = 0;
                        }
                        return true;// there is another entry in this page
                    }
                    if (_parent._cursor.Count == 0)
                        break;
                    _currentPage = _parent._cursor.Pop();
                }
                _currentPage = null;

                return false;
            }

            public ValueReader CreateReaderForCurrent()
            {
                if (_currentPage == null)
                    throw new InvalidOperationException("No current page was set");

                return  new ValueReader(_currentPage.Base + _currentPage.FixedSize_StartPosition + (_parent._entrySize * _currentPage.LastSearchPosition) + sizeof(long), _parent._valSize);
            
            }
        }
    }
}