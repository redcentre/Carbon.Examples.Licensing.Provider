using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

[Table("Customer")]
[Index("Name", Name = "IX_Customer_Name", IsUnique = true)]
public partial class Customer
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(32)]
    public string Name { get; set; }

    [StringLength(128)]
    public string DisplayName { get; set; }

    [Required]
    [StringLength(1024)]
    public string StorageKey { get; set; }

    [StringLength(1024)]
    public string Note { get; set; }

    [InverseProperty("Customer")]
    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    [ForeignKey("CustomerId")]
    [InverseProperty("Customers")]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}

public sealed class CustomerComparer : IEqualityComparer<Customer>
{
	public bool Equals(Customer x, Customer y) => x?.Id == y?.Id;

	public int GetHashCode([DisallowNull] Customer obj) => obj.Id.GetHashCode();
}