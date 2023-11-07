using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

[Table("Realm")]
[Index("Name", Name = "IX_Realm_Name", IsUnique = true)]
public partial class Realm
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(16)]
    public string Name { get; set; }

    public bool Inactive { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime Created { get; set; }

    [Column(TypeName = "xml")]
    public string Policy { get; set; }

    [ForeignKey("RealmId")]
    [InverseProperty("Realms")]
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    [ForeignKey("RealmId")]
    [InverseProperty("Realms")]
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
