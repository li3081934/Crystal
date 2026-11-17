using System.IO;

namespace Server.MirDatabase
{
    /// <summary>
    /// Flyway-style bookkeeping record for a database migration that has been applied.
    /// Stored in Server.MirDB so the same migration file is never applied twice.
    /// </summary>
    public class MigrationRecord
    {
        public int Version;
        public string Name = string.Empty;
        public string Checksum = string.Empty;
        public long AppliedAt;

        public MigrationRecord()
        {
        }

        public MigrationRecord(BinaryReader reader)
        {
            Version = reader.ReadInt32();
            Name = reader.ReadString();
            Checksum = reader.ReadString();
            AppliedAt = reader.ReadInt64();
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(Version);
            writer.Write(Name ?? string.Empty);
            writer.Write(Checksum ?? string.Empty);
            writer.Write(AppliedAt);
        }
    }
}
