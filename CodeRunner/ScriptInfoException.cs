using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeRunner
{
    class ScriptInfoException : Exception
    {
        public ScriptInfoException(string message)
            : base(message)
        {
        }
    }
}
