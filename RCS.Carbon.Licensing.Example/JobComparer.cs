using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using RCS.Carbon.Licensing.Example.EFCore;

namespace RCS.Carbon.Licensing.Example;

public sealed class JobComparer : IEqualityComparer<Job>
{
	public bool Equals(Job x, Job y) => x?.Id == y?.Id;

	public int GetHashCode([DisallowNull] Job obj) => obj.Id.GetHashCode();
}