using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

/// <summary>
/// Example licensing provider representation of a User in a SQL server table.
/// </summary>
[Table("User")]
[Index("Name", Name = "IX_User_Name", IsUnique = true)]
public partial class User
{
	[Key]
	public int Id { get; set; }

	[Required]
	[StringLength(128)]
	public string Name { get; set; }

	[StringLength(128)]
	public string ProviderId { get; set; }

	[StringLength(64)]
	public string Psw { get; set; }

	public byte[] PassHash { get; set; }

	[StringLength(128)]
	public string Email { get; set; }

	[StringLength(16)]
	public string EntityId { get; set; }

	[StringLength(256)]
	public string CloudCustomerNames { get; set; }

	[StringLength(256)]
	public string JobNames { get; set; }

	[StringLength(256)]
	public string VartreeNames { get; set; }

	[StringLength(256)]
	public string DashboardNames { get; set; }

	public int? DataLocation { get; set; }

	public int? Sequence { get; set; }

	public Guid Uid { get; set; }

	[StringLength(2000)]
	public string Comment { get; set; }

	[StringLength(128)]
	public string Roles { get; set; }

	[StringLength(128)]
	public string Filter { get; set; }

	[StringLength(256)]
	public string LoginMacs { get; set; }

	public int? LoginCount { get; set; }

	public int? LoginMax { get; set; }

	public DateTime? LastLogin { get; set; }

	public DateTime? Sunset { get; set; }

	public int? MaxJobs { get; set; }

	[StringLength(32)]
	public string Version { get; set; }

	[StringLength(32)]
	public string MinVersion { get; set; }

	public bool IsDisabled { get; set; }

	public DateTime Created { get; set; }

	[ForeignKey("UserId")]
	[InverseProperty("Users")]
	public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

	[ForeignKey("UserId")]
	[InverseProperty("Users")]
	public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
