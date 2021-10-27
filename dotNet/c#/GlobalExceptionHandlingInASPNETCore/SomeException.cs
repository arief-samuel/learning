using System;
using System.Globalization;

namespace GlobalExceptionHandlingInASPNETCore
{
    public class SomeException : Exception
    {
        public SomeException() : base()
        {
        }
        public SomeException(string message) : base(message)
        {
        }
        public SomeException(string message, params object[] args) : base(string.Format(CultureInfo.CurrentCulture, message, args))
        {
        }
    }
}