using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AGVServer.EFModels
{
    public partial class AGVDBContext : DbContext
    {
        public AGVDBContext()
        {
        }

        public AGVDBContext(DbContextOptions<AGVDBContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Configuration> Configurations { get; set; } = null!;
        public virtual DbSet<MxmodbusIndex> MxmodbusIndices { get; set; } = null!;
        public virtual DbSet<Plcconfig> Plcconfigs { get; set; } = null!;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see http://go.microsoft.com/fwlink/?LinkId=723263.
                optionsBuilder.UseSqlServer("Server=localhost;Database=AGVDB;Trusted_Connection=True; trustServerCertificate=true;");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Configuration>(entity =>
            {
                entity.HasKey(e => e.ConfigName)
                    .HasName("PK_configuration");

                entity.ToTable("Configuration");

                entity.Property(e => e.ConfigName)
                    .HasMaxLength(50)
                    .HasColumnName("configName");

                entity.Property(e => e.ConfigValue)
                    .HasMaxLength(50)
                    .HasColumnName("configValue");
            });

            modelBuilder.Entity<MxmodbusIndex>(entity =>
            {
                entity.HasKey(e => new { e.Plctype, e.VariableType, e.MxIndex, e.Offset });

                entity.ToTable("MXModbusIndex");

                entity.Property(e => e.Plctype)
                    .HasMaxLength(50)
                    .HasColumnName("PLCType");

                entity.Property(e => e.VariableType)
                    .HasMaxLength(10)
                    .HasColumnName("variableType")
                    .IsFixedLength();

                entity.Property(e => e.MxIndex)
                    .HasColumnType("numeric(18, 0)")
                    .HasColumnName("mxIndex");

                entity.Property(e => e.Offset)
                    .HasColumnType("numeric(18, 0)")
                    .HasColumnName("offset");

                entity.Property(e => e.Category)
                    .HasMaxLength(50)
                    .HasColumnName("category");

                entity.Property(e => e.Remark)
                    .HasMaxLength(50)
                    .HasColumnName("remark");

                entity.Property(e => e.UpdateType).HasColumnName("updateType");
            });

            modelBuilder.Entity<Plcconfig>(entity =>
            {
                entity.HasKey(e => e.Ip);

                entity.ToTable("PLCConfig");

                entity.Property(e => e.Ip)
                    .HasMaxLength(50)
                    .HasColumnName("ip");

                entity.Property(e => e.AlignSide).HasColumnName("alignSide");

                entity.Property(e => e.Enabled).HasColumnName("enabled");

                entity.Property(e => e.ModbusStartAddress)
                    .HasColumnType("numeric(18, 0)")
                    .HasColumnName("modbusStartAddress");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");

                entity.Property(e => e.No)
                    .HasColumnType("numeric(18, 0)")
                    .HasColumnName("no");

                entity.Property(e => e.Plctype)
                    .HasMaxLength(50)
                    .HasColumnName("PLCType");

                entity.Property(e => e.Port)
                    .HasColumnType("numeric(18, 0)")
                    .HasColumnName("port");

                entity.Property(e => e.Type)
                    .HasMaxLength(50)
                    .HasColumnName("type");
            });

            OnModelCreatingPartial(modelBuilder);
        }

        partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
    }
}
