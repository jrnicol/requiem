using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Requiem.Data.Configurations;

internal static class Lengths
{
    public const int Name = 200;
    public const int Short = 500;
    public const int Url = 2048;
    public const int UserId = 450; // ASP.NET Core Identity key length

    public static PropertyBuilder<T> HaveNameLength<T>(this PropertyBuilder<T> property) =>
        property.HasMaxLength(Name);
}
