using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace RCS.Carbon.Licensing.Example.EFCore;

[Table("User")]
[Index("Name", Name = "IX_User_Name", IsUnique = true)]
public partial class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(128)]
    public string Name { get; set; }

    [StringLength(64)]
    public string DisplayName { get; set; }

    [StringLength(128)]
    public string Email { get; set; }

    [StringLength(32)]
    public string Password { get; set; }

    [StringLength(1024)]
    public string Note { get; set; }

    [ForeignKey("UserId")]
    [InverseProperty("Users")]
    public virtual ICollection<Customer> Customers { get; set; } = new List<Customer>();

    [ForeignKey("UserId")]
    [InverseProperty("Users")]
    public virtual ICollection<Job> Jobs { get; set; } = new List<Job>();
}
