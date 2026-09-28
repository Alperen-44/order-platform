using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace InventoryService.Persistence;

public static class PostgresErrors
{
    public static bool IsUniqueViolation(this DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
