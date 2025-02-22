﻿using System;

namespace Sparrow.LowMemory
{
    public sealed class LowMemoryException : Exception
    {
        public LowMemoryException()
        {
        }

        public LowMemoryException(string message) : base(message)
        {
        }

        public LowMemoryException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
