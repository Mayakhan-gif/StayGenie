using StayGenie.Data;
using StayGenie.Services;

namespace StayGenie.Tests;

public class ReviewRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 10);

    [Fact]
    public void CanReview_ConfirmedStayFinished_NotReviewed_ReturnsTrue()
    {
        Assert.True(BookingRules.CanReview(ReservationStatus.Confirmed, Today.AddDays(-1), Today, false));
    }

    [Fact]
    public void CanReview_CheckOutIsToday_ReturnsTrue()
    {
        Assert.True(BookingRules.CanReview(ReservationStatus.Confirmed, Today, Today, false));
    }

    [Fact]
    public void CanReview_StayNotFinishedYet_ReturnsFalse()
    {
        Assert.False(BookingRules.CanReview(ReservationStatus.Confirmed, Today.AddDays(1), Today, false));
    }

    [Fact]
    public void CanReview_AlreadyReviewed_ReturnsFalse()
    {
        Assert.False(BookingRules.CanReview(ReservationStatus.Confirmed, Today.AddDays(-5), Today, true));
    }

    [Theory]
    [InlineData(ReservationStatus.PendingPayment)]
    [InlineData(ReservationStatus.Cancelled)]
    [InlineData(ReservationStatus.Expired)]
    public void CanReview_NotConfirmed_ReturnsFalse(ReservationStatus status)
    {
        Assert.False(BookingRules.CanReview(status, Today.AddDays(-5), Today, false));
    }
}