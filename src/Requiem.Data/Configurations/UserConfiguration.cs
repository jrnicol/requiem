using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Requiem.Data.Entities;

namespace Requiem.Data.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<RequiemUser>
{
    public void Configure(EntityTypeBuilder<RequiemUser> b) => b.Property(x => x.DisplayName).HasMaxLength(100);
}
