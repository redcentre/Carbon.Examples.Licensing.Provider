using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

/// <summary>
/// Example licensing provider representation of a Job in a SQL server table.
/// </summary>
[Table("Job")]
[Index("Name", Name = "IX_Job_Name")]
public partial class Job
{
	[Key]
	public int Id { get; set; }

	[Required]
	[StringLength(32)]
	public string Name { get; set; }

	[StringLength(128)]
	public string DisplayName { get; set; }

	public int? CustomerId { get; set; }

	[StringLength(128)]
	public string VartreeNames { get; set; }

	public int? DataLocation { get; set; }

	public int? Sequence { get; set; }

	public int? Cases { get; set; }

	public DateTime? LastUpdate { get; set; }

	[StringLength(1024)]
	public string Description { get; set; }

	[StringLength(1024)]
	public string Info { get; set; }

	[StringLength(256)]
	public string Logo { get; set; }

	[StringLength(256)]
	public string Url { get; set; }

	public bool IsMobile { get; set; }

	public bool DashboardsFirst { get; set; }

	public bool Inactive { get; set; }

	public DateTime Created { get; set; }

	[ForeignKey("CustomerId")]
	[InverseProperty("Jobs")]
	public virtual Customer Customer { get; set; }

	[ForeignKey("JobId")]
	[InverseProperty("Jobs")]
	public virtual ICollection<User> Users { get; set; } = new List<User>();
}

public sealed class JobComparer : IEqualityComparer<Job>
{
	public bool Equals(Job x, Job y) => x?.Id == y?.Id;

	public int GetHashCode([DisallowNull] Job obj) => obj.Id.GetHashCode();
}