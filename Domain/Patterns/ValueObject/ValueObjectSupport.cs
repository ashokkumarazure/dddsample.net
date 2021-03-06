using System;
using System.Collections;

using DomainDrivenDelivery.Utilities;

namespace DomainDrivenDelivery.Domain.Patterns.ValueObject
{
    /// <summary>
    /// Supporting base class for value objects.
    /// </summary>
    /// <remarks>
    /// While the <see cref="ValueObject{T}"/> interface makes the pattern properties explicit,
    /// this class is less general and is suited for this particular application.
    /// <para />
    /// For example, the private id field is meant for autogenerated, surrogate primary keys.
    /// Also, you may want more flexibility in selecting significant fields for comparision,
    /// or you may need to be able to calculate equals millions of times every second,
    /// in which case reflection might not be fast enough.
    /// </remarks>
    /// <typeparam name="T">The value object type.</typeparam>
    public abstract class ValueObjectSupport<T> : ValueObject<T> where T : ValueObjectSupport<T>
    {
        private readonly long _primaryKey;

        public virtual bool sameValueAs(T other)
        {
            return other != null && EqualsBuilder.reflectionEquals(typeof(T), this, other);
        }

        public override int GetHashCode()
        {
            return HashCodeBuilder.reflectionHashCode(this);
        }

        public override bool Equals(object obj)
        {
            if(obj == null) return false;
            if(this == obj) return true;
            if(!typeof(T).IsAssignableFrom(obj.GetType())) return false;

            return sameValueAs((T)obj);
        }
    }
}