using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    [StringLength(32)]
    public string Psw { get; set; }

    [Required]
    [StringLength(1024)]
    public string StorageKey { get; set; }

    [StringLength(256)]
    public string CloudCustomerNames { get; set; }

    public int? DataLocation { get; set; }

    public int? Sequence { get; set; }

    [StringLength(64)]
    public string Corporation { get; set; }

    [StringLength(2000)]
    public string Comment { get; set; }

    [StringLength(1024)]
    public string Info { get; set; }

    [StringLength(256)]
    public string Logo { get; set; }

    [StringLength(256)]
    public string SignInLogo { get; set; }

    [StringLength(1024)]
    public string SignInNote { get; set; }

    public int? Credits { get; set; }

    public int? Spent { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? Sunset { get; set; }

    public int? MaxJobs { get; set; }

    public bool Inactive { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    [InverseProperty("Customer")]
    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();

    [ForeignKey("CustomerId")]
    [InverseProperty("Customers")]
    public virtual ICollection<Realm> Realms { get; set; } = new List<Realm>();

    [ForeignKey("CustomerId")]
    [InverseProperty("Customers")]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
