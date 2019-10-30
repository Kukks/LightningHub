using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LightningHub.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public virtual DbSet<Transaction> Transactions { get; set; }
        
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.Entity<ApplicationUser>()
                .Property(e => e.Addresses)
                .HasConversion(
                    v => string.Join(',', v),
                    v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());
        }
    }

    public class ApplicationUser:IdentityUser
    {
        public string PartnerId { get; set; }
        public string AccountType { get; set; }
        
        public List<Transaction> Transactions { get; set; }
        public List<string> Addresses { get; set; }
        public long Balance { get; set; }
    }
    public class Transaction
    {
        public string Id { get; set; }
        public string UserId { get; set; }
        public string TransactionId { get; set; }
        public string Destination { get; set; }
        public long Amount { get; set; }
        public long Fee { get; set; }
        public DateTime Timestamp { get; set; }
        public PaymentType PaymentType { get; set; }
        public string DataJson { get; set; }
        public TransactionStatus Status { get; set; }
        public TransferType TransferType { get; set; }
    }

    public enum TransferType
    {
        Send,
        Receive
    }
    public enum PaymentType
    {
        Onchain,
        Offchain
    }

    public enum TransactionStatus
    {
        Pending,
        Complete,
        Expired,
        Cancelled
    }
}