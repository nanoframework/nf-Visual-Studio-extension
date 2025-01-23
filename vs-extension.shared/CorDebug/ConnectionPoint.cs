//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using Microsoft.VisualStudio.OLE.Interop;
using System;
using System.Collections;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public class ConnectionPoint : IConnectionPoint
    {
        public class Connections : IEnumerable
        {
            private uint m_dwCookieNext;
            private Hashtable m_ht;

            public Connections()
            {
                m_dwCookieNext = 0;
                m_ht = new Hashtable();
            }

            public void Advise(object pUnkSink, out uint pdwCookie)
            {
                pdwCookie = m_dwCookieNext++;            
                m_ht[pdwCookie] = pUnkSink;
            }

            public void Unadvise(uint dwCookie)
            {
                m_ht.Remove(dwCookie);
            }

            #region IEnumerable Members

            public IEnumerator GetEnumerator()
            {
                ArrayList al = new ArrayList ();

                foreach (object o in m_ht.Values)
                {
                    al.Add (o);
                }

                return al.GetEnumerator ();
            }
            
            #endregion
        }

        private IConnectionPointContainer m_container;
        private Guid m_iid;
        public readonly Connections m_connections;
        
        public ConnectionPoint(IConnectionPointContainer container, Guid iid)
        {
            m_container = container;
            m_iid = iid;
            m_connections = new Connections();
        }

        public Connections Sinks
        {
            get {return m_connections;}
        }

        #region IConnectionPoint Members

        public void Advise(object pUnkSink, out uint pdwCookie)
        {
            m_connections.Advise(pUnkSink, out pdwCookie);
        }

        public void Unadvise(uint dwCookie)
        {
            m_connections.Unadvise(dwCookie);
        }

        public void GetConnectionInterface(out Guid pIID)
        {
            pIID = m_iid;
        }

        public void EnumConnections(out IEnumConnections ppEnum)
        {            
            throw new NotImplementedException();           
        }

        public void GetConnectionPointContainer(out IConnectionPointContainer ppCPC)
        {
            ppCPC = m_container;
        }

        #endregion
    }
}
