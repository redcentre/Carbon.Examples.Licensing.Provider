using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RCS.Carbon.Licensing.Example.EFCore;

namespace RCS.Carbon.Licensing.Example;

public sealed class CustomerComparer : IEqualityComparer<Customer>
{
	public bool Equals(Customer x, Customer y) => x?.Id == y?.Id;

	public int GetHashCode([DisallowNull] Customer obj) => obj.Id.GetHashCode();
}