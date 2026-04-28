using Microsoft.EntityFrameworkCore;
using SpaBookingSystem.ApplicationCore.Entities;
using SpaBookingSystem.ApplicationCore.Constants;

namespace SpaBookingSystem.DataLayer;

public class SpaDbContext : DbContext
{
    public SpaDbContext(DbContextOptions<SpaDbContext> options) : base(options) { }

    public DbSet<Service> Services => Set<Service>();
    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Admin> Admins => Set<Admin>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingDetail> BookingDetails => Set<BookingDetail>();
    public DbSet<BookingDetailStaffAssignment> BookingDetailStaffAssignments => Set<BookingDetailStaffAssignment>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Staff> Staffs => Set<Staff>();
    public DbSet<StaffServiceCategory> StaffServiceCategories => Set<StaffServiceCategory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ServiceCategory>(e =>
        {
            e.ToTable("service_categories");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("category_id");
            e.Property(x => x.Name).HasColumnName("category_name")
                .HasMaxLength(DataLengths.NAME)
                .IsRequired();

            e.Property(x => x.Description).HasColumnName("description")
                .HasMaxLength(DataLengths.DESCRIPTION);

            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<Service>(e =>
        {
            e.ToTable("services");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("service_id");

            e.Property(x => x.Name).HasColumnName("service_name")
                .HasMaxLength(DataLengths.NAME)
                .IsRequired();

            e.Property(x => x.Description).HasColumnName("description")
                .HasMaxLength(DataLengths.DESCRIPTION);

            e.Property(x => x.Price).HasColumnName("price")
                .HasColumnType("decimal(10,2)");

            e.Property(x => x.Duration).HasColumnName("duration");

            e.Property(x => x.Status).HasColumnName("status")
                .HasMaxLength(DataLengths.STATUS);

            e.Property(x => x.ImageUrl).HasColumnName("image_url")
                .HasMaxLength(DataLengths.IMAGE_URL);

            e.Property(x => x.SlotCapacity).HasColumnName("slot_capacity");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.Property(x => x.CategoryId).HasColumnName("category_id");

            e.HasOne(x => x.Category)
                .WithMany(c => c.Services)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Customer>(e =>
        {
            e.ToTable("customers");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("customer_id");

            e.Property(x => x.FullName).HasColumnName("full_name")
                .HasMaxLength(DataLengths.NAME)
                .IsRequired();

            e.Property(x => x.Email).HasColumnName("email")
                .HasMaxLength(DataLengths.EMAIL)
                .IsRequired();

            e.Property(x => x.Phone).HasColumnName("phone")
                .HasMaxLength(20);

            e.Property(x => x.PasswordHash).HasColumnName("password_hash")
                .HasMaxLength(DataLengths.PASSWORD_HASH)
                .IsRequired();

            e.Property(x => x.Role).HasColumnName("role")
                .HasMaxLength(DataLengths.ROLE_NAME)
                .IsRequired();

            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.Phone).IsUnique();
        });

        modelBuilder.Entity<Admin>(e =>
        {
            e.ToTable("admins");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("admin_id");

            e.Property(x => x.FullName).HasColumnName("full_name")
                .HasMaxLength(DataLengths.NAME)
                .IsRequired();

            e.Property(x => x.Email).HasColumnName("email")
                .HasMaxLength(DataLengths.EMAIL)
                .IsRequired();

            e.Property(x => x.PasswordHash).HasColumnName("password_hash")
                .HasMaxLength(DataLengths.PASSWORD_HASH)
                .IsRequired();

            e.Property(x => x.Role).HasColumnName("role")
                .HasMaxLength(DataLengths.ROLE_NAME)
                .IsRequired();

            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasIndex(x => x.Email).IsUnique();
        });

        modelBuilder.Entity<Staff>(e =>
        {
            e.ToTable("staff");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("staff_id");
            e.Property(x => x.FullName).HasColumnName("full_name")
                .HasMaxLength(DataLengths.NAME)
                .IsRequired();

            e.Property(x => x.Email).HasColumnName("email")
                .HasMaxLength(DataLengths.EMAIL);

            e.Property(x => x.Phone).HasColumnName("phone")
                .HasMaxLength(20);

            e.Property(x => x.Skills).HasColumnName("skills")
                .HasMaxLength(DataLengths.SHORT_DESCRIPTION);

            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.MaxConcurrent).HasColumnName("max_concurrent");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");

            e.HasIndex(x => x.Email).IsUnique().HasFilter("[email] IS NOT NULL");
            e.HasIndex(x => x.Phone).IsUnique().HasFilter("[phone] IS NOT NULL");
        });

        modelBuilder.Entity<StaffServiceCategory>(e =>
        {
            e.ToTable("staff_service_categories");
            e.HasKey(x => new { x.StaffId, x.CategoryId });

            e.HasOne(x => x.Staff)
                .WithMany(x => x.StaffCategories)
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Category)
                .WithMany()
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Booking>(e =>
        {
            e.ToTable("bookings");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("booking_id");
            e.Property(x => x.BookingCode).HasColumnName("booking_code")
                .HasMaxLength(50)
                .IsRequired();

            e.Property(x => x.FullName).HasColumnName("full_name")
                .HasMaxLength(DataLengths.NAME)
                .IsRequired();

            e.Property(x => x.Phone).HasColumnName("phone")
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.Email).HasColumnName("email")
                .HasMaxLength(DataLengths.EMAIL)
                .IsRequired();

            e.Property(x => x.AppointmentDate).HasColumnName("appointment_date");
            e.Property(x => x.AppointmentTime).HasColumnName("appointment_time")
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.Note).HasColumnName("note")
                .HasMaxLength(DataLengths.DESCRIPTION);

            e.Property(x => x.TotalAmount).HasColumnName("total_amount")
                .HasColumnType("decimal(10,2)");

            e.Property(x => x.Status).HasColumnName("status")
                .HasMaxLength(DataLengths.STATUS)
                .IsRequired();

            e.Property(x => x.PaymentStatus).HasColumnName("payment_status")
                .HasMaxLength(DataLengths.STATUS)
                .IsRequired();

            e.Property(x => x.IsGroupBooking).HasColumnName("is_group_booking");
            e.Property(x => x.GroupSize).HasColumnName("group_size");
            e.Property(x => x.IsCheckedIn).HasColumnName("is_checked_in");
            e.Property(x => x.CheckedInAt).HasColumnName("checked_in_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        modelBuilder.Entity<BookingDetail>(e =>
        {
            e.ToTable("booking_details");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("booking_detail_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");
            e.Property(x => x.ServiceId).HasColumnName("service_id");
            e.Property(x => x.Quantity).HasColumnName("quantity");
            e.Property(x => x.AppointmentDate).HasColumnName("appointment_date").HasColumnType("date");
            e.Property(x => x.AppointmentTime).HasColumnName("appointment_time")
                .HasMaxLength(20)
                .IsRequired();

            e.Property(x => x.UnitPrice).HasColumnName("unit_price")
                .HasColumnType("decimal(10,2)");

            e.Property(x => x.LineTotal).HasColumnName("line_total")
                .HasColumnType("decimal(10,2)");

            e.HasOne(x => x.Booking)
                .WithMany(x => x.BookingDetails)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Service)
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BookingDetailStaffAssignment>(e =>
        {
            e.ToTable("booking_detail_staff_assignments");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("assignment_id");
            e.Property(x => x.BookingDetailId).HasColumnName("booking_detail_id");
            e.Property(x => x.StaffId).HasColumnName("staff_id");
            e.Property(x => x.AssignedQuantity).HasColumnName("assigned_quantity");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");

            e.HasIndex(x => new { x.BookingDetailId, x.StaffId }).IsUnique();

            e.HasOne(x => x.BookingDetail)
                .WithMany(x => x.StaffAssignments)
                .HasForeignKey(x => x.BookingDetailId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Staff)
                .WithMany(x => x.BookingDetailStaffAssignments)
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasColumnName("payment_id");
            e.Property(x => x.BookingId).HasColumnName("booking_id");

            e.Property(x => x.PaymentCode).HasColumnName("payment_code")
                .HasMaxLength(50)
                .IsRequired();

            e.Property(x => x.Method).HasColumnName("method")
                .HasMaxLength(50)
                .IsRequired();

            e.Property(x => x.Amount).HasColumnName("amount")
                .HasColumnType("decimal(10,2)");

            e.Property(x => x.Status).HasColumnName("status")
                .HasMaxLength(DataLengths.STATUS)
                .IsRequired();

            e.Property(x => x.PaidAt).HasColumnName("paid_at");

            e.Property(x => x.TransactionCode).HasColumnName("transaction_code")
                .HasMaxLength(100);

            e.HasOne(x => x.Booking)
                .WithMany(x => x.Payments)
                .HasForeignKey(x => x.BookingId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
