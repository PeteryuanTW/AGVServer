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

        public virtual DbSet<CellConfig> CellConfigs { get; set; } = null!;
        public virtual DbSet<Configuration> Configurations { get; set; } = null!;
        public virtual DbSet<DockingConfig> DockingConfigs { get; set; } = null!;
        public virtual DbSet<GroupConfig> GroupConfigs { get; set; } = null!;
        public virtual DbSet<ImesTask> ImesTasks { get; set; } = null!;
        public virtual DbSet<ManualStationConfig> ManualStationConfigs { get; set; } = null!;
        public virtual DbSet<MesTaskDetail> MesTaskDetails { get; set; } = null!;
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
            modelBuilder.Entity<CellConfig>(entity =>
            {
                entity.HasKey(e => e.CellName);

                entity.ToTable("CellConfig");

                entity.Property(e => e.CellName)
                    .HasMaxLength(50)
                    .HasColumnName("cellName");

                entity.Property(e => e.AlignSide).HasColumnName("alignSide");
            });

            modelBuilder.Entity<Configuration>(entity =>
            {
                entity.HasKey(e => new { e.Category, e.ConfigName })
                    .HasName("PK_Configuration_1");

                entity.ToTable("Configuration");

                entity.Property(e => e.Category).HasColumnName("category");

                entity.Property(e => e.ConfigName)
                    .HasMaxLength(50)
                    .HasColumnName("configName");

                entity.Property(e => e.ConfigValue)
                    .HasMaxLength(50)
                    .HasColumnName("configValue");
            });

            modelBuilder.Entity<DockingConfig>(entity =>
            {
                entity.HasKey(e => new { e.Name, e.TopOrBut });

                entity.ToTable("DockingConfig");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");

                entity.Property(e => e.TopOrBut).HasColumnName("topOrBut");

                entity.Property(e => e.GoalName)
                    .HasMaxLength(50)
                    .HasColumnName("goalName");
            });

            modelBuilder.Entity<GroupConfig>(entity =>
            {
                entity.HasKey(e => e.GroupName);

                entity.ToTable("GroupConfig");

                entity.Property(e => e.GroupName)
                    .HasMaxLength(50)
                    .HasColumnName("groupName");

                entity.Property(e => e.Elements)
                    .HasMaxLength(50)
                    .HasColumnName("elements");

                entity.Property(e => e.Occupied).HasColumnName("occupied");
            });

            modelBuilder.Entity<ImesTask>(entity =>
            {
                entity.HasKey(e => e.TaskNoFromMes);

                entity.ToTable("IMesTask");

                entity.Property(e => e.TaskNoFromMes).HasMaxLength(50);

                entity.Property(e => e.Barcode).HasMaxLength(50);

                entity.Property(e => e.FromStation).HasMaxLength(50);

                entity.Property(e => e.ToStation).HasMaxLength(50);
            });

            modelBuilder.Entity<ManualStationConfig>(entity =>
            {
                entity.HasKey(e => e.Name);

                entity.ToTable("ManualStationConfig");

                entity.Property(e => e.Name)
                    .HasMaxLength(50)
                    .HasColumnName("name");

                entity.Property(e => e.CheckBarcode).HasColumnName("checkBarcode");

                entity.Property(e => e.No)
                    .HasColumnType("numeric(18, 0)")
                    .HasColumnName("no");
            });

            modelBuilder.Entity<MesTaskDetail>(entity =>
            {
                entity.HasKey(e => e.TaskNoFromMes)
                    .HasName("PK_MesTask");

                entity.ToTable("MesTaskDetail");

                entity.Property(e => e.TaskNoFromMes).HasMaxLength(50);

                entity.Property(e => e.Amrid)
                    .HasMaxLength(50)
                    .HasColumnName("AMRID");

                entity.Property(e => e.AmrtoLoaderHighOrLow).HasColumnName("AMRToLoaderHighOrLow");

                entity.Property(e => e.AssignToSwarmCoreTime).HasMaxLength(50);

                entity.Property(e => e.Barcode).HasMaxLength(50);

                entity.Property(e => e.CustomInfo).HasColumnName("customInfo");

                entity.Property(e => e.FailTime).HasMaxLength(50);

                entity.Property(e => e.FinishOrTimeoutTime).HasMaxLength(50);

                entity.Property(e => e.FinishReason).HasMaxLength(50);

                entity.Property(e => e.FromStation).HasMaxLength(50);

                entity.Property(e => e.GetFromMesTime).HasMaxLength(50);

                entity.Property(e => e.LoaderToAmrhighOrLow).HasColumnName("LoaderToAMRHighOrLow");

                entity.Property(e => e.SwarmCoreActualStratTime).HasMaxLength(50);

                entity.Property(e => e.TaskNoFromSwarmCore).HasMaxLength(50);

                entity.Property(e => e.TaskType).HasColumnName("taskType");

                entity.Property(e => e.ToStation).HasMaxLength(50);
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

                entity.Property(e => e.CheckBarcode).HasColumnName("checkBarcode");

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
