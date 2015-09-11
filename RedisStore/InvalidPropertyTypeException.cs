using System;

namespace RedisStore
{
    public class InvalidPropertyTypeException : Exception
    {
        private readonly PropertyImplementationInfo _propInfo;

        public override string Message => $"{_propInfo.DeclaringType.Name}.{_propInfo.Name} has an invalid type. {ShittyGlobalClass.ValidTypeList}";

        internal InvalidPropertyTypeException(PropertyImplementationInfo propInfo)
        {
            _propInfo = propInfo;
        }
    }
}