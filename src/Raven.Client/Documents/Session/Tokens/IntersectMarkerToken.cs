﻿using System.Text;

namespace Raven.Client.Documents.Session.Tokens
{
    internal sealed class IntersectMarkerToken : QueryToken
    {
        private IntersectMarkerToken()
        {
        }

        public static readonly IntersectMarkerToken Instance = new IntersectMarkerToken();

        public override void WriteTo(StringBuilder writer)
        {
            writer.Append(",");
        }
    }
}
