using System.Data;
using Dixon.CommandCenter.API.Models;
using Dixon.CommandCenter.API.Services;
using Microsoft.Data.SqlClient;

namespace Dixon.CommandCenter.API.Data;

public sealed class ViewerAccessRepository(SqlConnectionFactory connectionFactory)
{
    public async Task<ViewerAccessConfigurationResponse?> GetConfigurationAsync(
        int viewerId,
        int? administratorPlantId,
        bool isSuperAdmin,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT va.AllPlants, va.AllCustomers
            FROM [User] u
            INNER JOIN Role r ON r.RoleId = u.RoleId
            LEFT JOIN ViewerAccess va ON va.UserId = u.UserId
            WHERE u.UserId = @ViewerId
              AND r.RoleName = 'Viewer'
              AND (@IsSuperAdmin = 1 OR u.PlantId = @AdministratorPlantId);
            """,
            connection);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        AddAdministratorScope(command, administratorPlantId, isSuperAdmin);

        bool configured;
        bool allPlants;
        bool allCustomers;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            configured = !reader.IsDBNull(0);
            allPlants = configured && reader.GetBoolean(0);
            allCustomers = configured && reader.GetBoolean(1);
        }

        var plantIds = new List<int>();
        await using (var plantCommand = new SqlCommand(
            """
            SELECT vap.PlantId
            FROM ViewerAccessPlant vap
            INNER JOIN ViewerAccess va ON va.UserId = vap.UserId
            WHERE vap.UserId = @ViewerId
            ORDER BY vap.PlantId;
            """,
            connection))
        {
            Add(plantCommand, "@ViewerId", SqlDbType.Int, viewerId);
            await using var reader = await plantCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                plantIds.Add(reader.GetInt32(0));
            }
        }

        var customerIds = new List<int>();
        await using (var customerCommand = new SqlCommand(
            """
            SELECT vac.CustomerId
            FROM ViewerAccessCustomer vac
            INNER JOIN ViewerAccess va ON va.UserId = vac.UserId
            WHERE vac.UserId = @ViewerId
              AND va.AllPlants = 1
            ORDER BY vac.CustomerId;
            """,
            connection))
        {
            Add(customerCommand, "@ViewerId", SqlDbType.Int, viewerId);
            await using var reader = await customerCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                customerIds.Add(reader.GetInt32(0));
            }
        }

        var plantCustomerAssignments = new List<ViewerPlantCustomerAssignmentRequest>();
        await using (var assignmentCommand = new SqlCommand(
            """
            SELECT vapc.PlantId, vapc.CustomerId
            FROM ViewerAccessPlantCustomer vapc
            INNER JOIN ViewerAccess va ON va.UserId = vapc.UserId
            WHERE vapc.UserId = @ViewerId
              AND va.AllPlants = 0
            ORDER BY vapc.PlantId, vapc.CustomerId;
            """,
            connection))
        {
            Add(assignmentCommand, "@ViewerId", SqlDbType.Int, viewerId);
            await using var reader = await assignmentCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                plantCustomerAssignments.Add(new ViewerPlantCustomerAssignmentRequest(
                    reader.GetInt32(0),
                    reader.GetInt32(1)));
            }
        }

        if (!isSuperAdmin
            && (allPlants
                || plantIds.Any(id => id != administratorPlantId)
                || plantCustomerAssignments.Any(assignment => assignment.PlantId != administratorPlantId)
                || (allPlants && !allCustomers
                    && !await CustomersRemainInPlantAsync(
                        connection,
                        administratorPlantId.GetValueOrDefault(),
                        customerIds,
                        cancellationToken))))
        {
            throw new ViewerAccessForbiddenException();
        }

        return new ViewerAccessConfigurationResponse(
            configured,
            allPlants,
            allCustomers,
            plantIds,
            customerIds,
            plantCustomerAssignments);
    }

    public async Task<IReadOnlyDictionary<int, ViewerAccessSummary>> GetSummariesAsync(
        int? administratorPlantId,
        bool isSuperAdmin,
        CancellationToken cancellationToken)
    {
        var summaries = new Dictionary<int, MutableSummary>();
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using (var command = new SqlCommand(
            """
            SELECT u.UserId, va.AllPlants, va.AllCustomers
            FROM [User] u
            INNER JOIN Role r ON r.RoleId = u.RoleId
            LEFT JOIN ViewerAccess va ON va.UserId = u.UserId
            WHERE r.RoleName = 'Viewer'
              AND (@IsSuperAdmin = 1 OR u.PlantId = @AdministratorPlantId);
            """,
            connection))
        {
            AddAdministratorScope(command, administratorPlantId, isSuperAdmin);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                summaries[reader.GetInt32(0)] = new MutableSummary(
                    !reader.IsDBNull(1),
                    !reader.IsDBNull(1) && reader.GetBoolean(1),
                    !reader.IsDBNull(1) && reader.GetBoolean(2));
            }
        }

        await ReadSummaryNamesAsync(
            connection,
            """
            SELECT vap.UserId, p.PlantName
            FROM ViewerAccessPlant vap
            INNER JOIN ViewerAccess va ON va.UserId = vap.UserId
            INNER JOIN [User] u ON u.UserId = vap.UserId
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN Plant p ON p.PlantId = vap.PlantId
            WHERE (@IsSuperAdmin = 1 OR (u.PlantId = @AdministratorPlantId AND p.PlantId = @AdministratorPlantId))
            ORDER BY p.PlantName;
            """,
            administratorPlantId,
            isSuperAdmin,
            summaries,
            (summary, name) => summary.PlantNames.Add(name),
            cancellationToken);
        await ReadSummaryNamesAsync(
            connection,
            """
            SELECT vac.UserId, c.CustomerName
            FROM ViewerAccessCustomer vac
            INNER JOIN ViewerAccess va ON va.UserId = vac.UserId
            INNER JOIN [User] u ON u.UserId = vac.UserId
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN Customer c ON c.CustomerId = vac.CustomerId
            WHERE va.AllPlants = 1
              AND (@IsSuperAdmin = 1 OR
                   (u.PlantId = @AdministratorPlantId
                    AND EXISTS (SELECT 1 FROM PlantCustomer pc
                                WHERE pc.PlantId = @AdministratorPlantId
                                  AND pc.CustomerId = c.CustomerId)))
            ORDER BY c.CustomerName;
            """,
            administratorPlantId,
            isSuperAdmin,
            summaries,
            (summary, name) => summary.CustomerNames.Add(name),
            cancellationToken);
        await ReadSummaryNamesAsync(
            connection,
            """
            SELECT vapc.UserId, CONCAT(p.PlantName, ' — ', c.CustomerName)
            FROM ViewerAccessPlantCustomer vapc
            INNER JOIN ViewerAccess va ON va.UserId = vapc.UserId
            INNER JOIN [User] u ON u.UserId = vapc.UserId
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN Plant p ON p.PlantId = vapc.PlantId
            INNER JOIN Customer c ON c.CustomerId = vapc.CustomerId
            WHERE (@IsSuperAdmin = 1 OR
                   (u.PlantId = @AdministratorPlantId
                    AND vapc.PlantId = @AdministratorPlantId))
            ORDER BY p.PlantName, c.CustomerName;
            """,
            administratorPlantId,
            isSuperAdmin,
            summaries,
            (summary, name) => summary.CustomerNames.Add(name),
            cancellationToken);

        return summaries.ToDictionary(
            item => item.Key,
            item => new ViewerAccessSummary(
                item.Value.IsConfigured,
                item.Value.AllPlants,
                item.Value.AllCustomers,
                item.Value.PlantNames,
                item.Value.CustomerNames));
    }

    public async Task<ViewerAccessScope?> GetScopeAsync(
        int viewerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        bool allPlants;
        bool allCustomers;
        await using (var command = new SqlCommand(
            """
            SELECT va.AllPlants, va.AllCustomers
            FROM [User] u
            INNER JOIN Role r ON r.RoleId = u.RoleId
            INNER JOIN ViewerAccess va ON va.UserId = u.UserId
            WHERE u.UserId = @ViewerId
              AND u.IsActive = 1
              AND r.RoleName = 'Viewer';
            """,
            connection))
        {
            Add(command, "@ViewerId", SqlDbType.Int, viewerId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            allPlants = reader.GetBoolean(0);
            allCustomers = reader.GetBoolean(1);
        }

        var plantIds = await ReadIdsAsync(
            connection,
            "SELECT PlantId FROM ViewerAccessPlant WHERE UserId = @UserId;",
            viewerId,
            cancellationToken);
        var customerIds = await ReadIdsAsync(
            connection,
            """
            SELECT vac.CustomerId
            FROM ViewerAccessCustomer vac
            INNER JOIN ViewerAccess va ON va.UserId = vac.UserId
            WHERE vac.UserId = @UserId AND va.AllPlants = 1;
            """,
            viewerId,
            cancellationToken);
        var plantCustomerAssignments = await ReadPlantCustomerAssignmentsAsync(
            connection,
            viewerId,
            cancellationToken);

        return new ViewerAccessScope(
            true,
            allPlants,
            allCustomers,
            plantIds.ToHashSet(),
            customerIds.ToHashSet(),
            plantCustomerAssignments
                .Select(assignment => (assignment.PlantId, assignment.CustomerId))
                .ToHashSet());
    }

    public async Task<bool> IsActivePlantAsync(
        int plantId,
        ViewerAccessScope scope,
        int viewerId,
        CancellationToken cancellationToken)
    {
        if (!ViewerAccessScopePolicy.CanAccessPlant(scope, plantId))
        {
            return false;
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT COUNT(1)
            FROM Plant p
            INNER JOIN ViewerAccess va ON va.UserId = @ViewerId
            WHERE p.PlantId = @PlantId
              AND p.IsActive = 1
              AND (va.AllPlants = 1 OR EXISTS
                   (SELECT 1 FROM ViewerAccessPlant vap
                    WHERE vap.UserId = va.UserId AND vap.PlantId = p.PlantId));
            """,
            connection);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<bool> HasCustomerAccessAsync(
        int viewerId,
        int plantId,
        int customerId,
        ViewerAccessScope scope,
        CancellationToken cancellationToken)
    {
        if (!ViewerAccessScopePolicy.CanAccessCustomer(scope, plantId, customerId, true))
        {
            return false;
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            """
            SELECT COUNT(1)
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            INNER JOIN Plant p ON p.PlantId = pc.PlantId
            INNER JOIN ViewerAccess va ON va.UserId = @ViewerId
            WHERE pc.PlantId = @PlantId
              AND pc.CustomerId = @CustomerId
              AND c.IsActive = 1
              AND p.IsActive = 1
              AND (va.AllPlants = 1 OR EXISTS
                   (SELECT 1 FROM ViewerAccessPlant vap
                    WHERE vap.UserId = va.UserId AND vap.PlantId = pc.PlantId))
              AND (va.AllCustomers = 1
                   OR (va.AllPlants = 1 AND EXISTS
                       (SELECT 1 FROM ViewerAccessCustomer vac
                        WHERE vac.UserId = va.UserId AND vac.CustomerId = pc.CustomerId))
                   OR (va.AllPlants = 0 AND EXISTS
                       (SELECT 1 FROM ViewerAccessPlantCustomer vapc
                        WHERE vapc.UserId = va.UserId
                          AND vapc.PlantId = pc.PlantId
                          AND vapc.CustomerId = pc.CustomerId)));
            """,
            connection);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        Add(command, "@CustomerId", SqlDbType.Int, customerId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<ViewerAccessConfigurationResponse?> SaveAsync(
        int viewerId,
        int administratorId,
        int administratorPlantId,
        bool isSuperAdmin,
        ViewerAccessUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var plantIds = request.PlantIds?.Distinct().ToArray() ?? [];
        var customerIds = request.CustomerIds?.Distinct().ToArray() ?? [];
        var plantCustomerAssignments = request.PlantCustomerAssignments?
            .Distinct()
            .ToArray() ?? [];
        if (plantIds.Any(id => id <= 0)
            || customerIds.Any(id => id <= 0)
            || plantCustomerAssignments.Any(assignment =>
                assignment.PlantId <= 0 || assignment.CustomerId <= 0))
        {
            throw new ViewerAccessValidationException(
                "Plant and customer IDs must be greater than zero.");
        }

        if (request.AllPlants && !isSuperAdmin)
        {
            throw new ViewerAccessForbiddenException();
        }

        if (request.AllPlants && plantIds.Length > 0)
        {
            throw new ViewerAccessValidationException(
                "Do not submit specific plants when All Plants is enabled.");
        }

        if (!request.AllPlants && plantIds.Length == 0)
        {
            throw new ViewerAccessValidationException(
                "Select at least one plant or enable All Plants.");
        }

        if (request.AllPlants && plantCustomerAssignments.Length > 0)
        {
            throw new ViewerAccessValidationException(
                "Do not submit plant-specific customers when All Plants is enabled.");
        }

        if (!request.AllPlants && customerIds.Length > 0)
        {
            throw new ViewerAccessValidationException(
                "Submit customers with their plants when Specific Plants is enabled.");
        }

        if (request.AllCustomers
            && (customerIds.Length > 0 || plantCustomerAssignments.Length > 0))
        {
            throw new ViewerAccessValidationException(
                "Do not submit specific customers when All Customers is enabled.");
        }

        if (!request.AllCustomers
            && (request.AllPlants ? customerIds.Length == 0 : plantCustomerAssignments.Length == 0))
        {
            throw new ViewerAccessValidationException(
                "Select at least one customer per plant or enable All Customers.");
        }

        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            await using var targetCommand = new SqlCommand(
                """
                SELECT u.PlantId
                FROM [User] u WITH (UPDLOCK, HOLDLOCK)
                INNER JOIN Role r ON r.RoleId = u.RoleId
                WHERE u.UserId = @ViewerId
                  AND r.RoleName = 'Viewer'
                  AND (@IsSuperAdmin = 1 OR u.PlantId = @AdministratorPlantId);
                """,
                connection,
                transaction);
            Add(targetCommand, "@ViewerId", SqlDbType.Int, viewerId);
            Add(targetCommand, "@IsSuperAdmin", SqlDbType.Bit, isSuperAdmin);
            Add(targetCommand, "@AdministratorPlantId", SqlDbType.Int, administratorPlantId);
            var targetPlant = await targetCommand.ExecuteScalarAsync(cancellationToken);
            if (targetPlant is null or DBNull)
            {
                await transaction.RollbackAsync(cancellationToken);
                return null;
            }

            if (!isSuperAdmin
                && !request.AllPlants
                && (plantIds.Any(id => id != administratorPlantId)
                    || plantCustomerAssignments.Any(assignment =>
                        assignment.PlantId != administratorPlantId)))
            {
                throw new ViewerAccessForbiddenException();
            }

            var validPlantCount = 0;
            if (!request.AllPlants)
            {
                await using var plantValidation = CreateIdValidationCommand(
                    connection,
                    transaction,
                    "Plant",
                    "PlantId",
                    plantIds,
                    "IsActive = 1");
                validPlantCount = Convert.ToInt32(
                    await plantValidation.ExecuteScalarAsync(cancellationToken));
                if (validPlantCount != plantIds.Length)
                {
                    throw new ViewerAccessValidationException(
                        "Select only active plants available in your administration scope.");
                }
            }

            if (!request.AllCustomers && request.AllPlants)
            {
                await using var customerValidation = new SqlCommand(
                    BuildCustomerValidationSql(customerIds, true, []),
                    connection,
                    transaction);
                AddIdParameters(customerValidation, "CustomerId", customerIds);
                var validCustomers = Convert.ToInt32(
                    await customerValidation.ExecuteScalarAsync(cancellationToken));
                if (validCustomers != customerIds.Length)
                {
                    throw new ViewerAccessValidationException(
                        "Every selected customer must be active and mapped to an assigned plant.");
                }
            }

            if (!request.AllCustomers && !request.AllPlants)
            {
                if (plantCustomerAssignments.Any(
                    assignment => !plantIds.Contains(assignment.PlantId)))
                {
                    throw new ViewerAccessValidationException(
                        "Every customer assignment must belong to a selected plant.");
                }

                await using var assignmentValidation = new SqlCommand(
                    BuildPlantCustomerValidationSql(plantCustomerAssignments),
                    connection,
                    transaction);
                AddPlantCustomerParameters(assignmentValidation, plantCustomerAssignments);
                var validAssignments = Convert.ToInt32(
                    await assignmentValidation.ExecuteScalarAsync(cancellationToken));
                if (validAssignments != plantCustomerAssignments.Length)
                {
                    throw new ViewerAccessValidationException(
                        "Every selected customer must be active and mapped to its assigned plant.");
                }
            }

            await using (var clearPlantCustomerAssignments = new SqlCommand(
                "DELETE FROM ViewerAccessPlantCustomer WHERE UserId = @ViewerId;",
                connection,
                transaction))
            {
                Add(clearPlantCustomerAssignments, "@ViewerId", SqlDbType.Int, viewerId);
                await clearPlantCustomerAssignments.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var upsert = new SqlCommand(
                """
                IF EXISTS (SELECT 1 FROM ViewerAccess WHERE UserId = @ViewerId)
                    UPDATE ViewerAccess
                    SET AllPlants = @AllPlants,
                        AllCustomers = @AllCustomers,
                        ConfiguredAt = SYSUTCDATETIME(),
                        ConfiguredBy = @AdministratorId
                    WHERE UserId = @ViewerId;
                ELSE
                    INSERT INTO ViewerAccess (UserId, AllPlants, AllCustomers, ConfiguredBy)
                    VALUES (@ViewerId, @AllPlants, @AllCustomers, @AdministratorId);
                """,
                connection,
                transaction))
            {
                Add(upsert, "@ViewerId", SqlDbType.Int, viewerId);
                Add(upsert, "@AllPlants", SqlDbType.Bit, request.AllPlants);
                Add(upsert, "@AllCustomers", SqlDbType.Bit, request.AllCustomers);
                Add(upsert, "@AdministratorId", SqlDbType.Int, administratorId);
                await upsert.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var clearPlants = new SqlCommand(
                "DELETE FROM ViewerAccessPlant WHERE UserId = @ViewerId;",
                connection,
                transaction))
            {
                Add(clearPlants, "@ViewerId", SqlDbType.Int, viewerId);
                await clearPlants.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!request.AllPlants)
            {
                await InsertAssignmentsAsync(
                    connection,
                    transaction,
                    "ViewerAccessPlant",
                    "PlantId",
                    viewerId,
                    plantIds,
                    cancellationToken);
            }

            await using (var clearCustomers = new SqlCommand(
                "DELETE FROM ViewerAccessCustomer WHERE UserId = @ViewerId;",
                connection,
                transaction))
            {
                Add(clearCustomers, "@ViewerId", SqlDbType.Int, viewerId);
                await clearCustomers.ExecuteNonQueryAsync(cancellationToken);
            }

            if (!request.AllCustomers)
            {
                if (request.AllPlants)
                {
                    await InsertAssignmentsAsync(
                        connection,
                        transaction,
                        "ViewerAccessCustomer",
                        "CustomerId",
                        viewerId,
                        customerIds,
                        cancellationToken);
                }
                else
                {
                    await InsertPlantCustomerAssignmentsAsync(
                        connection,
                        transaction,
                        viewerId,
                        plantCustomerAssignments,
                        cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return await GetConfigurationAsync(
            viewerId,
            administratorPlantId,
            isSuperAdmin,
            cancellationToken);
    }

    private static string BuildCustomerValidationSql(
        IReadOnlyList<int> customerIds,
        bool allPlants,
        IReadOnlyList<int> plantIds)
    {
        var customerParameters = string.Join(
            ", ",
            Enumerable.Range(0, customerIds.Count).Select(index => $"@CustomerId{index}"));
        var plantParameters = string.Join(
            ", ",
            Enumerable.Range(0, plantIds.Count).Select(index => $"@PlantId{index}"));

        var plantScope = allPlants
            ? "EXISTS (SELECT 1 FROM PlantCustomer selectedPc INNER JOIN Plant activePlant ON activePlant.PlantId = selectedPc.PlantId WHERE selectedPc.CustomerId = c.CustomerId AND activePlant.IsActive = 1)"
            : $"EXISTS (SELECT 1 FROM PlantCustomer selectedPc WHERE selectedPc.CustomerId = c.CustomerId AND selectedPc.PlantId IN ({plantParameters}))";

        return $"""
            SELECT COUNT(DISTINCT c.CustomerId)
            FROM Customer c
            WHERE c.IsActive = 1
              AND c.CustomerId IN ({customerParameters})
              AND {plantScope};
            """;
    }

    private static string BuildPlantCustomerValidationSql(
        IReadOnlyList<ViewerPlantCustomerAssignmentRequest> assignments)
    {
        var selectedPairs = string.Join(
            ", ",
            Enumerable.Range(0, assignments.Count)
                .Select(index => $"(@PlantId{index}, @CustomerId{index})"));

        return $"""
            SELECT COUNT(1)
            FROM (VALUES {selectedPairs}) selected(PlantId, CustomerId)
            INNER JOIN Plant p ON p.PlantId = selected.PlantId AND p.IsActive = 1
            INNER JOIN Customer c ON c.CustomerId = selected.CustomerId AND c.IsActive = 1
            INNER JOIN PlantCustomer pc
                ON pc.PlantId = selected.PlantId
               AND pc.CustomerId = selected.CustomerId;
            """;
    }

    private static async Task<bool> CustomersRemainInPlantAsync(
        SqlConnection connection,
        int plantId,
        IReadOnlyList<int> customerIds,
        CancellationToken cancellationToken)
    {
        if (customerIds.Count == 0)
        {
            return true;
        }

        var parameters = string.Join(
            ", ",
            Enumerable.Range(0, customerIds.Count).Select(index => $"@CustomerId{index}"));
        await using var command = new SqlCommand(
            $"""
            SELECT COUNT(DISTINCT pc.CustomerId)
            FROM PlantCustomer pc
            INNER JOIN Customer c ON c.CustomerId = pc.CustomerId
            WHERE pc.PlantId = @PlantId
              AND c.IsActive = 1
              AND pc.CustomerId IN ({parameters});
            """,
            connection);
        Add(command, "@PlantId", SqlDbType.Int, plantId);
        AddIdParameters(command, "CustomerId", customerIds);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken))
            == customerIds.Count;
    }

    private static async Task InsertAssignmentsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        string keyColumn,
        int viewerId,
        IReadOnlyList<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return;
        }

        var rows = string.Join(
            ", ",
            Enumerable.Range(0, ids.Count)
                .Select(index => $"(@ViewerId, @Id{index})"));
        await using var command = new SqlCommand(
            $"INSERT INTO {table} (UserId, {keyColumn}) VALUES {rows};",
            connection,
            transaction);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        AddIdParameters(command, "Id", ids);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertPlantCustomerAssignmentsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int viewerId,
        IReadOnlyList<ViewerPlantCustomerAssignmentRequest> assignments,
        CancellationToken cancellationToken)
    {
        if (assignments.Count == 0)
        {
            return;
        }

        var rows = string.Join(
            ", ",
            Enumerable.Range(0, assignments.Count)
                .Select(index => $"(@ViewerId, @PlantId{index}, @CustomerId{index})"));
        await using var command = new SqlCommand(
            $"INSERT INTO ViewerAccessPlantCustomer (UserId, PlantId, CustomerId) VALUES {rows};",
            connection,
            transaction);
        Add(command, "@ViewerId", SqlDbType.Int, viewerId);
        AddPlantCustomerParameters(command, assignments);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddPlantCustomerParameters(
        SqlCommand command,
        IReadOnlyList<ViewerPlantCustomerAssignmentRequest> assignments)
    {
        for (var index = 0; index < assignments.Count; index++)
        {
            Add(command, $"@PlantId{index}", SqlDbType.Int, assignments[index].PlantId);
            Add(command, $"@CustomerId{index}", SqlDbType.Int, assignments[index].CustomerId);
        }
    }

    private static SqlCommand CreateIdValidationCommand(
        SqlConnection connection,
        SqlTransaction transaction,
        string table,
        string idColumn,
        IReadOnlyList<int> ids,
        string additionalPredicate)
    {
        var parameters = string.Join(
            ", ",
            Enumerable.Range(0, ids.Count).Select(index => $"@PlantId{index}"));
        var command = new SqlCommand(
            $"SELECT COUNT(DISTINCT {idColumn}) FROM {table} WHERE {additionalPredicate} AND {idColumn} IN ({parameters});",
            connection,
            transaction);
        AddIdParameters(command, "PlantId", ids);
        return command;
    }

    private static void AddIdParameters(
        SqlCommand command,
        string prefix,
        IReadOnlyList<int> ids)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            Add(command, $"@{prefix}{index}", SqlDbType.Int, ids[index]);
        }
    }

    private static async Task<IReadOnlyList<int>> ReadIdsAsync(
        SqlConnection connection,
        string sql,
        int userId,
        CancellationToken cancellationToken)
    {
        var ids = new List<int>();
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetInt32(0));
        }

        return ids;
    }

    private static async Task<IReadOnlyList<ViewerPlantCustomerAssignmentRequest>>
        ReadPlantCustomerAssignmentsAsync(
            SqlConnection connection,
            int userId,
            CancellationToken cancellationToken)
    {
        var assignments = new List<ViewerPlantCustomerAssignmentRequest>();
        await using var command = new SqlCommand(
            """
            SELECT vapc.PlantId, vapc.CustomerId
            FROM ViewerAccessPlantCustomer vapc
            INNER JOIN ViewerAccess va ON va.UserId = vapc.UserId
            WHERE vapc.UserId = @UserId AND va.AllPlants = 0;
            """,
            connection);
        Add(command, "@UserId", SqlDbType.Int, userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            assignments.Add(new ViewerPlantCustomerAssignmentRequest(
                reader.GetInt32(0),
                reader.GetInt32(1)));
        }

        return assignments;
    }

    private static async Task ReadSummaryNamesAsync(
        SqlConnection connection,
        string sql,
        int? administratorPlantId,
        bool isSuperAdmin,
        IReadOnlyDictionary<int, MutableSummary> summaries,
        Action<MutableSummary, string> appendName,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(sql, connection);
        Add(command, "@IsSuperAdmin", SqlDbType.Bit, isSuperAdmin);
        Add(command, "@AdministratorPlantId", SqlDbType.Int, administratorPlantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (summaries.TryGetValue(reader.GetInt32(0), out var summary))
            {
                appendName(summary, reader.GetString(1));
            }
        }
    }

    private static void AddAdministratorScope(
        SqlCommand command,
        int? administratorPlantId,
        bool isSuperAdmin)
    {
        Add(command, "@IsSuperAdmin", SqlDbType.Bit, isSuperAdmin);
        Add(command, "@AdministratorPlantId", SqlDbType.Int, administratorPlantId);
    }

    private static void Add(
        SqlCommand command,
        string name,
        SqlDbType type,
        object? value)
    {
        command.Parameters.Add(name, type).Value = value ?? DBNull.Value;
    }

    private sealed class MutableSummary(
        bool isConfigured,
        bool allPlants,
        bool allCustomers)
    {
        public bool IsConfigured { get; } = isConfigured;
        public bool AllPlants { get; } = allPlants;
        public bool AllCustomers { get; } = allCustomers;
        public List<string> PlantNames { get; } = [];
        public List<string> CustomerNames { get; } = [];
    }
}

public sealed record ViewerAccessConfigurationResponse(
    bool Configured,
    bool AllPlants,
    bool AllCustomers,
    IReadOnlyList<int> PlantIds,
    IReadOnlyList<int> CustomerIds,
    IReadOnlyList<ViewerPlantCustomerAssignmentRequest> PlantCustomerAssignments);

public sealed class ViewerAccessValidationException(string message) : Exception(message);

public sealed class ViewerAccessForbiddenException()
    : Exception("You are not permitted to assign access outside your administration scope.");
