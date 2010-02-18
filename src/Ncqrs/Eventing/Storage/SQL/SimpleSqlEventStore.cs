﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

namespace Ncqrs.Eventing.Storage.SQL
{
    /// <summary>
    /// Stores events for a SQL database.
    /// </summary>
    public class SimpleMicrosoftSqlServerEventStore : IEventStore
    {
        #region Queries
        private const String DeleteUnusedProviders =
            @"DELETE FROM [EventSources] WHERE (SELECT Count(EventSourceId) FROM [Events] WHERE [EventSourceId]=[EventSources].[Id]) = 0";

        private const String InsertNewEventQuery =
            @"INSERT INTO [Events]([EventSourceId], [Name], [Data], [TimeStamp]) VALUES (@Id, @Name, @Data, getDate())";

        private const String InsertNewProviderQuery =
            @"INSERT INTO [EventSources](Id, Type, Version) VALUES (@Id, @Type, @Version)";

        private const String SelectAllEventsQuery =
            @"SELECT [TimeStamp], [Data] FROM [Events] WHERE [EventSourceId] = evntSourceId ORDER BY [TimeStamp]";

        private const String SelectAllIdsForTypeQuery = @"SELECT [Id] FROM [EventSources] WHERE [Type] = @Type";

        private const String SelectVersionQuery = @"SELECT [Version] FROM [EventSources] WHERE [Id] = @id";

        private const String UpdateEventSourceVersionQuery =
            @"UPDATE [EventSources] SET [Version] = (SELECT Count(*) FROM [Events] WHERE [EventSourceId] = @Id) WHERE [Id] = @id";
        #endregion

        private readonly String _connectionString;

        public SimpleMicrosoftSqlServerEventStore(String connectionString)
        {
            if(String.IsNullOrEmpty(connectionString)) throw new ArgumentNullException("connectionString");

            _connectionString = connectionString;
        }

        /// <summary>
        /// Get all event for a specific event provider.
        /// </summary>
        /// <param name="id">The id of the event provider.</param>
        /// <returns>All events for the specified event provider.</returns>
        public IEnumerable<HistoricalEvent> GetAllEventsForEventSource(Guid id)
        {
            // Create connection and command.
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(SelectAllEventsQuery, connection))
            {
                // Add EventSourceId parameter and open connection.
                command.Parameters.AddWithValue("EventSourceId", id);
                connection.Open();

                // Execute query and create reader.
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    // Create formatter that can deserialize our events.
                    var formatter = new BinaryFormatter();

                    while (reader.Read())
                    {
                        // Get event details.
                        var timeStamp = (DateTime)reader["TimeStamp"];
                        var rawData = (Byte[])reader["Data"];

                        using (var dataStream = new MemoryStream(rawData))
                        {
                            // Deserialize event and yield it.
                            var evnt = (IEvent)formatter.Deserialize(dataStream);
                            yield return new HistoricalEvent(timeStamp, evnt);
                        }
                    }

                    // Break the yield.
                    yield break;
                }
            }
        }

        /// <summary>
        /// Saves all events from an event provider.
        /// </summary>
        /// <param name="provider">The eventsource.</param>
        /// <returns>The events that are saved.</returns>
        public IEnumerable<IEvent> Save(EventSource eventSource)
        {
            // Get all events.
            IEnumerable<IEvent> events = eventSource.GetUncommitedEvents();

            // Create new connection.
            using (var connection = new SqlConnection(_connectionString))
            {
                // Open connection and begin a transaction so we can
                // commit or rollback all the changes that has been made.
                connection.Open();
                SqlTransaction transaction = connection.BeginTransaction();

                try
                {
                    // Get the current version of the event provider.
                    int? currentVersion = GetVersion(eventSource.Id, transaction);

                    // Create new event provider when it is not found.
                    if (currentVersion == null)
                    {
                        CreateEventSource(eventSource, transaction);
                    }
                    else if (currentVersion.Value != eventSource.Version)
                    {
                        throw new ConcurrencyException(eventSource.Version, currentVersion.Value);
                    }

                    // Save all events to the store.
                    SaveEvents(events, eventSource.Id, transaction);

                    // Update the version of the provider.
                    UpdateEventSourceVersion(eventSource, transaction);

                    // Everything is handled, commint transaction.
                    transaction.Commit();
                }
                catch
                {
                    // Something went wrong, rollback transaction.
                    transaction.Rollback();
                    throw;
                }
            }

            return events;
        }

        public IEnumerable<Guid> GetAllIdsForType(Type eventProviderType)
        {
            // Create connection and command.
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(SelectAllIdsForTypeQuery, connection))
            {
                // Add EventSourceId parameter and open connection.
                command.Parameters.AddWithValue("Type", eventProviderType.FullName);
                connection.Open();

                // Execute query and create reader.
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        yield return (Guid)reader[0];
                    }
                }
            }
        }

        public void RemoveUnusedProviders()
        {
            using (var connection = new SqlConnection(_connectionString))
            using (var command = new SqlCommand(DeleteUnusedProviders, connection))
            {
                connection.Open();

                try
                {
                    command.ExecuteNonQuery();
                }
                finally
                {
                    connection.Close();
                }
            }
        }

        private static void UpdateEventSourceVersion(EventSource eventSource, SqlTransaction transaction)
        {
            using (var command = new SqlCommand(UpdateEventSourceVersionQuery, transaction.Connection))
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("Id", eventSource.Id);
                command.ExecuteNonQuery();
            }
        }

        private static void SaveEvents(IEnumerable<IEvent> evnts, Guid providerId, SqlTransaction transaction)
        {
            foreach (IEvent evnt in evnts)
            {
                SaveEvent(evnt, providerId, transaction);
            }
        }

        private static void SaveEvent(IEvent evnt, Guid providerId, SqlTransaction transaction)
        {
            var dataStream = new MemoryStream();
            var formatter = new BinaryFormatter();
            formatter.Serialize(dataStream, evnt);
            byte[] data = dataStream.ToArray();

            using (var command = new SqlCommand(InsertNewEventQuery, transaction.Connection))
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("Id", providerId);
                command.Parameters.AddWithValue("Name", evnt.GetType().FullName);
                command.Parameters.AddWithValue("Data", data);
                command.ExecuteNonQuery();
            }
        }

        private static void CreateEventSource(EventSource eventSource, SqlTransaction transaction)
        {
            using (var command = new SqlCommand(InsertNewProviderQuery, transaction.Connection))
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("Id", eventSource.Id);
                command.Parameters.AddWithValue("Type", eventSource.GetType().ToString());
                command.Parameters.AddWithValue("Version", eventSource.Version);
                command.ExecuteNonQuery();
            }
        }

        private static int? GetVersion(Guid providerId, SqlTransaction transaction)
        {
            using (var command = new SqlCommand(SelectVersionQuery, transaction.Connection))
            {
                command.Transaction = transaction;
                command.Parameters.AddWithValue("id", providerId);
                return (int?)command.ExecuteScalar();
            }
        }

        public static IEnumerable<String> GetTableCreationQueries()
        {
            yield return @"CREATE TABLE [dbo].[Events]([EventProviderId] [uniqueidentifier] NOT NULL, [TimeStamp] [datetime] NOT NULL, [Data] [varbinary](max) NOT NULL, [Name] [varchar](max) NOT NULL) ON [PRIMARY]";
            yield return @"CREATE TABLE [dbo].[EventProviders]([Id] [uniqueidentifier] NOT NULL, [Type] [nvarchar](255) NOT NULL, [Version] [int] NOT NULL) ON [PRIMARY]";
        }
    }
}