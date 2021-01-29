//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using CorDebugInterop;
using nanoFramework.Tools.Debugger;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace nanoFramework.Tools.VisualStudio.Extension
{
    public abstract class CorDebugValue : ICorDebugHeapValue, ICorDebugValue2
    {
        protected RuntimeValue _rtv;
        protected CorDebugAppDomain _appDomain;

        public static CorDebugValue CreateValue(RuntimeValue rtv, CorDebugAppDomain appDomain)
        {
            CorDebugValue val = null;
            bool fIsReference;
            
            if (rtv.IsBoxed)
            {
                val = new CorDebugValueBoxedObject (rtv, appDomain);
                fIsReference = true;
            }
            else if (rtv.IsPrimitive)
            {
                CorDebugClass c = ClassFromRuntimeValue (rtv, appDomain);
    
                if (c.IsEnum)
                {
                    val = new CorDebugValueObject (rtv, appDomain);
                    fIsReference = false;
                }
                else
                {
                    val = new CorDebugValuePrimitive (rtv, appDomain);
                    fIsReference = false;
                }
            }
            else if (rtv.IsArray)
            {
                val = new CorDebugValueArray (rtv, appDomain);
                fIsReference = true;
            }
            else if (rtv.CorElementType == CorElementType.ELEMENT_TYPE_STRING)
            {
                val = new CorDebugValueString (rtv, appDomain);
                fIsReference = true;
            }
            else
            {
                val = new CorDebugValueObject (rtv, appDomain);
                fIsReference = !rtv.IsValueType;
            }
            
            if (fIsReference)
            {
                val = new CorDebugValueReference(val, val._rtv, val._appDomain);
            }

            if (rtv.IsReference)    //CorElementType.ELEMENT_TYPE_BYREF
            {
                val = new CorDebugValueReferenceByRef(val, val._rtv, val._appDomain);
            }

            return val;        
        }

        public static CorDebugValue[] CreateValues(RuntimeValue[] rtv, CorDebugAppDomain appDomain)
        {
            CorDebugValue [] values = new CorDebugValue[rtv.Length];
            for (int i = 0; i < rtv.Length; i++)
            {
                values[i] = CorDebugValue.CreateValue(rtv[i], appDomain);
            }

            return values;
        }

        public static CorDebugClass ClassFromRuntimeValue(RuntimeValue rtv, CorDebugAppDomain appDomain)
        {
            RuntimeValue_Reflection rtvf = rtv as RuntimeValue_Reflection;
            CorDebugClass cls = null;
            object objBuiltInKey = null;
            Debug.Assert( !rtv.IsNull );

            if (rtvf != null)
            {
                objBuiltInKey = rtvf.ReflectionType;
            }
            else if(rtv.DataType == nanoClrDataType.DATATYPE_TRANSPARENT_PROXY)
            {
                objBuiltInKey = nanoClrDataType.DATATYPE_TRANSPARENT_PROXY;
            }
            else
            {
                cls = nanoCLR_TypeSystem.CorDebugClassFromTypeIndex( rtv.Type, appDomain ); ;
            }

            if(objBuiltInKey != null)
            {                
                CorDebugProcess.BuiltinType builtInType = appDomain.Process.ResolveBuiltInType( objBuiltInKey );             
                
                cls = builtInType.GetClass( appDomain );

                if(cls == null)
                {
                    cls = new CorDebugClass( builtInType.GetAssembly( appDomain ), builtInType.TokenCLR );
                }                
            }

            return cls;
        }

        public CorDebugValue(RuntimeValue rtv, CorDebugAppDomain appDomain)
        {
            _rtv = rtv;                                  
            _appDomain = appDomain;
        }

        public virtual RuntimeValue RuntimeValue
        {
            get { return _rtv; }

            set
            {
                //This should only be used if the underlying RuntimeValue changes, but not the data
                //For example, if we ever support compaction.  For now, this is only used when the scratch
                //pad needs resizing, the RuntimeValues, and there associated heapblock*, will be relocated
                Debug.Assert (_rtv.GetType () == value.GetType ());
                Debug.Assert(_rtv.CorElementType == value.CorElementType || value.IsNull || _rtv.IsNull);
                //other debug checks here...
                _rtv = value;
            }
        }

        public CorDebugAppDomain AppDomain
        {
            get { return _appDomain; }
        }

        protected Engine Engine
        {
            [DebuggerHidden]
            get {return _appDomain.Engine;}
        }        

        protected CorDebugValue CreateValue(RuntimeValue rtv)
        {
            return CorDebugValue.CreateValue(rtv, _appDomain);
        }

        protected virtual CorElementType ElementTypeProtected
        {
            get { return _rtv.CorElementType; }
        }

        public virtual uint Size
        {
            get { return 8; }
        }

        public virtual CorElementType Type
        {
            get { return ElementTypeProtected; }
        }
    
        public ICorDebugValue ICorDebugValue
        {
            get { return (ICorDebugValue)this; }
        }

        public ICorDebugHeapValue ICorDebugHeapValue
        {
            get { return (ICorDebugHeapValue)this; }
        }

        #region ICorDebugValue Members

        int ICorDebugValue.GetType( out CorElementType pType )
        {
            pType = Type;

            return COM_HResults.S_OK;
        }

        int ICorDebugValue.GetSize( out uint pSize )
        {
            pSize = Size;

            return COM_HResults.S_OK;            
        }

        int ICorDebugValue.GetAddress( out ulong pAddress )
        {
            pAddress = _rtv.ReferenceIdDirect;

            return COM_HResults.S_OK; 
        }

        int ICorDebugValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            ppBreakpoint = null;

            return COM_HResults.E_NOTIMPL;            
        }

        #endregion

        #region ICorDebugHeapValue Members

        #region ICorDebugValue

        int ICorDebugHeapValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugHeapValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );            
        }

        int ICorDebugHeapValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugHeapValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugHeapValue

        int ICorDebugHeapValue.IsValid( out int pbValid )
        {
            pbValid = Boolean.TRUE;

            return COM_HResults.S_OK;            
        }

        int ICorDebugHeapValue.CreateRelocBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            ppBreakpoint = null;

            return COM_HResults.E_NOTIMPL;            
        }

        #endregion

        #endregion

        #region ICorDebugValue2 Members

        int ICorDebugValue2.GetExactType(out ICorDebugType ppType)
        {
            ppType = new CorDebugGenericType(RuntimeValue.CorElementType, _rtv, _appDomain);
            return COM_HResults.S_OK;
        }

        #endregion
    }

    public class CorDebugValuePrimitive : CorDebugValue, ICorDebugGenericValue
    {
        public CorDebugValuePrimitive(RuntimeValue rtv, CorDebugAppDomain appDomain) : base(rtv, appDomain)
        {
        }
        
        protected virtual object ValueProtected
        {
            get { return _rtv.Value; }
            set { _rtv.Value = value; }
        }

        public override uint Size
        {
            get 
            {
                object o = ValueProtected;
                return (uint)Marshal.SizeOf( o );
            }
        }

        public ICorDebugGenericValue ICorDebugGenericValue
        {
            get {return (ICorDebugGenericValue)this;}
        }

        #region ICorDebugGenericValue Members

        #region ICorDebugValue

        int ICorDebugGenericValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugGenericValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );         
        }

        int ICorDebugGenericValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugGenericValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        int ICorDebugGenericValue.GetValue( IntPtr pTo )
        {
            byte[] data = null;
            object val = ValueProtected;

            switch(ElementTypeProtected)
            {
                case CorElementType.ELEMENT_TYPE_BOOLEAN:
                    data = BitConverter.GetBytes( (bool)val );
                    break;

                case CorElementType.ELEMENT_TYPE_I1:
                    data = new byte[] { (byte)(sbyte)val };
                    break;

                case CorElementType.ELEMENT_TYPE_U1:
                    data = new byte[] { (byte)val };
                    break;

                case CorElementType.ELEMENT_TYPE_CHAR:
                    data = BitConverter.GetBytes( (char)val );
                    break;

                case CorElementType.ELEMENT_TYPE_I2:
                    data = BitConverter.GetBytes( (short)val );
                    break;

                case CorElementType.ELEMENT_TYPE_U2:
                    data = BitConverter.GetBytes( (ushort)val );
                    break;

                case CorElementType.ELEMENT_TYPE_I4:
                    data = BitConverter.GetBytes( (int)val );
                    break;

                case CorElementType.ELEMENT_TYPE_U4:
                    data = BitConverter.GetBytes( (uint)val );
                    break;

                case CorElementType.ELEMENT_TYPE_R4:
                    data = BitConverter.GetBytes( (float)val );
                    break;

                case CorElementType.ELEMENT_TYPE_I8:
                    data = BitConverter.GetBytes( (long)val );
                    break;

                case CorElementType.ELEMENT_TYPE_U8:
                    data = BitConverter.GetBytes( (ulong)val );
                    break;

                case CorElementType.ELEMENT_TYPE_R8:
                    data = BitConverter.GetBytes( (double)val );
                    break;

                default:
                    Debug.Assert( false );
                    break;
            }
            Marshal.Copy( data, 0, pTo, data.Length );

            return COM_HResults.S_OK;
        }

        int ICorDebugGenericValue.SetValue( IntPtr pFrom )
        {
            object val = null;
            uint cByte = Size;            

            byte[] data = new byte[cByte];

            Marshal.Copy( pFrom, data, 0, (int)cByte );
            switch(ElementTypeProtected)
            {
                case CorElementType.ELEMENT_TYPE_BOOLEAN:
                    val = BitConverter.ToBoolean( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_I1:
                    val = (sbyte)data[0];
                    break;

                case CorElementType.ELEMENT_TYPE_U1:
                    val = data[0];
                    break;

                case CorElementType.ELEMENT_TYPE_CHAR:
                    val = BitConverter.ToChar( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_I2:
                    val = BitConverter.ToInt16( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_U2:
                    val = BitConverter.ToUInt16( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_I4:
                    val = BitConverter.ToInt32( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_U4:
                    val = BitConverter.ToUInt32( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_R4:
                    val = BitConverter.ToSingle( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_I8:
                    val = BitConverter.ToInt64( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_U8:
                    val = BitConverter.ToUInt64( data, 0 );
                    break;

                case CorElementType.ELEMENT_TYPE_R8:
                    val = BitConverter.ToDouble( data, 0 );
                    break;
            }

            ValueProtected = val;

            return COM_HResults.S_OK;
        }

        #endregion
}
    
    public class CorDebugValueBoxedObject : CorDebugValue, ICorDebugBoxValue
    {
        CorDebugValueObject m_value;

        public CorDebugValueBoxedObject(RuntimeValue rtv, CorDebugAppDomain appDomain) : base (rtv, appDomain)
        {
            m_value = new CorDebugValueObject (rtv, appDomain);  
        }

        public override RuntimeValue RuntimeValue
        {
            set
            {
                m_value.RuntimeValue = value;
                base.RuntimeValue = value;
            }
        }

        public override CorElementType Type
        {
            get { return CorElementType.ELEMENT_TYPE_CLASS; }
        }

        #region ICorDebugBoxValue Members

        #region ICorDebugValue

        int ICorDebugBoxValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugBoxValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );            
        }

        int ICorDebugBoxValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugBoxValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugHeapValue

        int ICorDebugBoxValue.IsValid( out int pbValid )
        {
            return ICorDebugHeapValue.IsValid( out pbValid );            
        }

        int ICorDebugBoxValue.CreateRelocBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugBoxValue

        int ICorDebugBoxValue.GetObject( out ICorDebugObjectValue ppObject )
        {
            ppObject = m_value;

            return COM_HResults.S_OK;            
        }

        #endregion

        #endregion
    }

    public class CorDebugValueReference : CorDebugValue, ICorDebugHandleValue, ICorDebugValue2
    {
        private CorDebugValue m_value;

        public CorDebugValueReference( CorDebugValue val, RuntimeValue rtv, CorDebugAppDomain appDomain )
            : base( rtv, appDomain )
        {
            m_value = val;
        }

        public override RuntimeValue RuntimeValue
        {
            set
            {
                m_value.RuntimeValue = value;
                base.RuntimeValue = value;
            }
        }

        public override CorElementType Type
        {
            get 
            {
                return m_value.Type;                
            }
        }

        public ICorDebugReferenceValue ICorDebugReferenceValue
        {
            get { return (ICorDebugReferenceValue)this; }
        }

        #region ICorDebugReferenceValue Members

        #region ICorDebugValue2 Members

        int ICorDebugValue2.GetExactType(out ICorDebugType ppType)
        {
            return ((ICorDebugValue2)m_value).GetExactType( out ppType);
        }

        #endregion

        #region ICorDebugValue

        int ICorDebugReferenceValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugReferenceValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );            
        }

        int ICorDebugReferenceValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugReferenceValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );
        }

        #endregion

        #region ICorDebugReferenceValue

        int ICorDebugReferenceValue.IsNull( out int pbNull )
        {
            pbNull = Boolean.BoolToInt( _rtv.IsNull );

            return COM_HResults.S_OK;            
        }

        int ICorDebugReferenceValue.GetValue( out ulong pValue )
        {
            pValue = (ulong)_rtv.ReferenceIdDirect;

            return COM_HResults.S_OK;            
        }

        int ICorDebugReferenceValue.SetValue( ulong value )
        {
            Debug.Assert( value <= uint.MaxValue );

            RuntimeValue rtvNew = _rtv.Assign((uint)value);
            RuntimeValue = rtvNew;

            return COM_HResults.S_OK;            
        }

        int ICorDebugReferenceValue.Dereference( out ICorDebugValue ppValue )
        {
            ppValue = m_value;

            return COM_HResults.S_OK;            
        }

        int ICorDebugReferenceValue.DereferenceStrong( out ICorDebugValue ppValue )
        {
            return ICorDebugReferenceValue.Dereference( out ppValue );            
        }

        #endregion

        #endregion

        #region ICorDebugHandleValue Members

        #region ICorDebugValue

        int ICorDebugHandleValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugHandleValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );            
        }

        int ICorDebugHandleValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugHandleValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugReferenceValue

        int ICorDebugHandleValue.IsNull( out int pbNull )
        {
            return ICorDebugReferenceValue.IsNull( out pbNull );            
        }

        int ICorDebugHandleValue.GetValue( out ulong pValue )
        {
            return ICorDebugReferenceValue.GetValue( out pValue );            
        }

        int ICorDebugHandleValue.SetValue( ulong value )
        {
            return ICorDebugReferenceValue.SetValue( value );            
        }

        int ICorDebugHandleValue.Dereference( out ICorDebugValue ppValue )
        {
            return ICorDebugReferenceValue.Dereference( out ppValue );            
        }

        int ICorDebugHandleValue.DereferenceStrong( out ICorDebugValue ppValue )
        {
            return ICorDebugReferenceValue.DereferenceStrong( out ppValue );            
        }

        #endregion

        #region ICorDebugHandleValue

        int ICorDebugHandleValue.GetHandleType( out CorDebugHandleType pType )
        {
            pType = CorDebugHandleType.HANDLE_STRONG;

            return COM_HResults.S_OK;            
        }

        int ICorDebugHandleValue.Dispose()
        {
            return COM_HResults.S_OK;            
        }

        #endregion

        #endregion
    }
    
    public class CorDebugValueReferenceByRef : CorDebugValueReference
    {
        public CorDebugValueReferenceByRef(CorDebugValue val, RuntimeValue rtv, CorDebugAppDomain appDomain) : base(val, rtv, appDomain)
        {                    
        }

        public override CorElementType Type
        {
            get { return CorElementType.ELEMENT_TYPE_BYREF; }
        }
    }

    public class CorDebugValueArray : CorDebugValue, ICorDebugArrayValue, ICorDebugValue2
    {
        public CorDebugValueArray(RuntimeValue rtv, CorDebugAppDomain appDomain) : base(rtv, appDomain)
        {
        }

        public static CorElementType typeValue = CorElementType.ELEMENT_TYPE_I4;

        public uint Count
        {
            get { return _rtv.Length; }
        }

        public ICorDebugArrayValue ICorDebugArrayValue
        {
            get { return (ICorDebugArrayValue)this; }
        }

        #region ICorDebugArrayValue Members

        #region ICorDebugValue

        int ICorDebugArrayValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugArrayValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );            
        }

        int ICorDebugArrayValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugArrayValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugValue2 Members

        int ICorDebugValue2.GetExactType(out ICorDebugType ppType)

        {
            ppType = new CorDebugTypeArray( this );
            return COM_HResults.S_OK;
        }
        
        #endregion

        #region ICorDebugHeapValue

        int ICorDebugArrayValue.IsValid( out int pbValid )
        {
            return ICorDebugHeapValue.IsValid( out pbValid );            
        }

        int ICorDebugArrayValue.CreateRelocBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugArrayValue


        /* With implementation of ICorDebugValue2.GetExactType this function
         * ICorDebugArrayValue.GetElementType is not called.
         * It is left to support VS 2005.
         * We cannot remove this function since it is part of ICorDebugArrayValue interface.
         */
         // FIXME
        
        int ICorDebugArrayValue.GetElementType( out CorElementType pType )
        {
            if (Count != 0)
            {
                pType = _rtv.GetElement(0).CorElementType;
            }
            else
            {
                pType = CorElementType.ELEMENT_TYPE_CLASS;
            }
            return COM_HResults.S_OK;            
        }

        int ICorDebugArrayValue.GetRank( out uint pnRank )
        {
            pnRank = 1;

            return COM_HResults.S_OK;                 
        }

        int ICorDebugArrayValue.GetCount( out uint pnCount )
        {
            pnCount = Count;

            return COM_HResults.S_OK;            
        }

        int ICorDebugArrayValue.GetDimensions( uint cdim, uint[] dims )
        {
            Debug.Assert( cdim == 1 );
            
            dims[0] = Count;

            return COM_HResults.S_OK; 
        }

        int ICorDebugArrayValue.HasBaseIndicies( out int pbHasBaseIndicies )
        {
            pbHasBaseIndicies = Boolean.FALSE;

            return COM_HResults.S_OK;             
        }

        int ICorDebugArrayValue.GetBaseIndicies( uint cdim, uint[] indicies )
        {
            Debug.Assert( cdim == 1 );
            
            indicies[0] = 0;

            return COM_HResults.S_OK;
        }

        int ICorDebugArrayValue.GetElement( uint cdim, uint[] indices, out ICorDebugValue ppValue )
        {
            //ask for several at once and cache?

            Debug.Assert( cdim == 1 );

            ppValue = CreateValue(_rtv.GetElement(indices[0]));

            return COM_HResults.S_OK;   
        }

        int ICorDebugArrayValue.GetElementAtPosition( uint nPosition, out ICorDebugValue ppValue )
        {
            //Cache values?
            ppValue = CreateValue(_rtv.GetElement(nPosition));

            return COM_HResults.S_OK;
        }

        #endregion

        #endregion
}

    public class CorDebugValueObject : CorDebugValue, ICorDebugObjectValue, ICorDebugGenericValue /*, ICorDebugObjectValue2*/
    {
        CorDebugClass      m_class = null;        
        CorDebugValuePrimitive      m_valuePrimitive = null;     //for boxed primitives, or enums
        bool               m_fIsEnum;
        bool               m_fIsBoxed;

        //Object or CLASS, or VALUETYPE
        public CorDebugValueObject(RuntimeValue rtv, CorDebugAppDomain appDomain) : base(rtv, appDomain)
        {
            if(!rtv.IsNull)
            {
                m_class = CorDebugValue.ClassFromRuntimeValue(rtv, appDomain);            
                m_fIsEnum = m_class.IsEnum;
                m_fIsBoxed = rtv.IsBoxed;                
            }
        }

        private bool IsValuePrimitive()
        {
            if (m_fIsBoxed || m_fIsEnum)
            {
                if (m_valuePrimitive == null)
                {
                    if (_rtv.IsBoxed)
                    {
                        RuntimeValue rtv = _rtv.GetField(1, 0);

                        Debug.Assert(rtv.IsPrimitive);

                        //Assert that m_class really points to a primitive
                        m_valuePrimitive = (CorDebugValuePrimitive)CreateValue(rtv);
                    }
                    else
                    {
                        Debug.Assert(m_fIsEnum);
                        m_valuePrimitive = new CorDebugValuePrimitive(_rtv, _appDomain);
                        Debug.Assert(_rtv.IsPrimitive);
                    }
                }
            }

            return m_valuePrimitive != null;
        }
        
        public override uint Size
        {
            get 
            {
                if (IsValuePrimitive())
                {
                    return m_valuePrimitive.Size;
                }
                else
                {
                    return 4;
                }
            }
        }

        public override CorElementType Type
        {
            get 
            {
                if(m_fIsEnum)
                {
                    return CorElementType.ELEMENT_TYPE_VALUETYPE;
                }
                else
                {
                    return base.Type;                    
                }
            }
        }


        #region ICorDebugObjectValue Members

        #region ICorDebugValue

        int ICorDebugObjectValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );            
        }

        int ICorDebugObjectValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );        
        }

        int ICorDebugObjectValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugObjectValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugObjectValue

        int ICorDebugObjectValue.GetClass( out ICorDebugClass ppClass )
        {
            ppClass = m_class;

            return COM_HResults.S_OK;            
        }

        int ICorDebugObjectValue.GetFieldValue( ICorDebugClass pClass, uint fieldDef, out ICorDebugValue ppValue )
        {
            //cache fields?
            RuntimeValue rtv = _rtv.GetField(0, nanoCLR_TypeSystem.ClassMemberIndexFromCLRToken(fieldDef, ((CorDebugClass)pClass).Assembly));

            // sanity check
            if(rtv == null)
            {
                MessageCentre.InternalErrorMessage($"ERROR: Failed to get value for field [{fieldDef.ToString("X8")}] assembly {((CorDebugClass)pClass).Assembly.Name}.");
                
                ppValue = null;

                return COM_HResults.E_FAIL;
            }

            ppValue = CreateValue( rtv );

            return COM_HResults.S_OK;                 
        }

        int ICorDebugObjectValue.GetVirtualMethod( uint memberRef, out ICorDebugFunction ppFunction )
        {
            uint mdVirtual = Engine.GetVirtualMethod(nanoCLR_TypeSystem.ClassMemberIndexFromCLRToken(memberRef, m_class.Assembly), _rtv);

            ppFunction = nanoCLR_TypeSystem.CorDebugFunctionFromMethodIndex( mdVirtual, _appDomain);

            return COM_HResults.S_OK;                             
        }

        int ICorDebugObjectValue.GetContext( out ICorDebugContext ppContext )
        {
            ppContext = null;

            return COM_HResults.S_OK;            
        }

        int ICorDebugObjectValue.IsValueClass( out int pbIsValueClass )
        {
            pbIsValueClass = Boolean.BoolToInt( _rtv.IsValueType );

            return COM_HResults.S_OK;                      
        }

        int ICorDebugObjectValue.GetManagedCopy( out object ppObject )
        {
            ppObject = null;

            Debug.Assert( false );

            return COM_HResults.S_OK;            
        }

        int ICorDebugObjectValue.SetFromManagedCopy( object pObject )
        {
            return COM_HResults.S_OK;              
        }

        #endregion

        #endregion

        #region ICorDebugGenericValue Members

        #region ICorDebugValue

        int ICorDebugGenericValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );             
        }

        int ICorDebugGenericValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );            
        }

        int ICorDebugGenericValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );            
        }

        int ICorDebugGenericValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );            
        }

        #endregion

        #region ICorDebugGenericValue

        int ICorDebugGenericValue.GetValue( IntPtr pTo )
        {
            int hr = COM_HResults.S_OK;

            if (IsValuePrimitive())
            {
                hr = m_valuePrimitive.ICorDebugGenericValue.GetValue(pTo);
            }
            else
            {
                ulong addr;

                hr = ((ICorDebugGenericValue)this).GetAddress(out addr);

                Marshal.WriteInt32(pTo, (int)addr);
            }

            return hr;            
        }

        int ICorDebugGenericValue.SetValue( IntPtr pFrom )
        {
            int hr = COM_HResults.S_OK;

            if (IsValuePrimitive())
            {
                hr = m_valuePrimitive.ICorDebugGenericValue.SetValue(pFrom);
            }
            else
            {
                Debug.Assert(false);
            }

            return hr;
        }

        #endregion

        #endregion
    }

    public class CorDebugValueString : CorDebugValue, ICorDebugStringValue
    {
        public CorDebugValueString( RuntimeValue rtv, CorDebugAppDomain appDomain )
            : base( rtv, appDomain )
        {
        }

        private string Value
        {
            get
            {
                string ret = _rtv.Value as string;
                return (ret == null) ? "" : ret;
            }
        }


        #region ICorDebugStringValue Members

        #region ICorDebugValue

        int ICorDebugStringValue.GetType( out CorElementType pType )
        {
            return ICorDebugValue.GetType( out pType );
        }

        int ICorDebugStringValue.GetSize( out uint pSize )
        {
            return ICorDebugValue.GetSize( out pSize );   
        }

        int ICorDebugStringValue.GetAddress( out ulong pAddress )
        {
            return ICorDebugValue.GetAddress( out pAddress );
        }

        int ICorDebugStringValue.CreateBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugValue.CreateBreakpoint( out ppBreakpoint );
        }

        #endregion

        #region ICorDebugHeapValue

        int ICorDebugStringValue.IsValid( out int pbValid )
        {
            return ICorDebugHeapValue.IsValid( out pbValid );
        }

        int ICorDebugStringValue.CreateRelocBreakpoint( out ICorDebugValueBreakpoint ppBreakpoint )
        {
            return ICorDebugHeapValue.CreateRelocBreakpoint( out ppBreakpoint );
        }

        #endregion

        #region ICorDebugStringValue

        int ICorDebugStringValue.GetLength( out uint pcchString )
        {
            pcchString = (uint)Value.Length;

            return COM_HResults.S_OK;            
        }

        int ICorDebugStringValue.GetString( uint cchString, IntPtr pcchString, IntPtr szString )
        {
            Utility.MarshalString( Value, cchString, pcchString, szString, false );

            return COM_HResults.S_OK;            
        }

        #endregion

        #endregion

    }
}
