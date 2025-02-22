﻿using System;

namespace Raven.Client.Exceptions.Database
{
    public sealed class DatabaseConcurrentLoadTimeoutException : RavenException
    {
        public DatabaseConcurrentLoadTimeoutException(string message)
            : base(message)
        {
        }

        public DatabaseConcurrentLoadTimeoutException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
