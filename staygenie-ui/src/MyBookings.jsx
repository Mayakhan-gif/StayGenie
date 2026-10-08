import { useEffect, useState, useCallback } from "react";
import { api } from "./api";
import "./Dashboard.css";

const pkr = (n) => `PKR ${Number(n ?? 0).toLocaleString()}`;

export default function MyBookings({ auth, onUnauthorized }) {
  const [summary, setSummary] = useState(null);
  const [bookings, setBookings] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [reviewFor, setReviewFor] = useState(null);
  const [rating, setRating] = useState(5);
  const [comment, setComment] = useState("");

  const load = useCallback(async () => {
    try {
      const [s, b] = await Promise.all([
        api("/api/me/summary", { token: auth.token }),
        api("/api/me/reservations", { token: auth.token }),
      ]);
      setSummary(s);
      setBookings(b);
    } catch (e) {
      if (e.status === 401) onUnauthorized();
      else setError(e.message);
    } finally {
      setLoading(false);
    }
  }, [auth.token, onUnauthorized]);

  useEffect(() => {
    load();
  }, [load]);

  async function cancel(id) {
    if (!window.confirm("Cancel this booking?")) return;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await api(`/api/me/reservations/${id}/cancel`, { token: auth.token, method: "POST" });
      setNotice("Booking cancelled.");
      await load();
    } catch (e) {
      if (e.status === 401) onUnauthorized();
      else setError(e.message);
    } finally {
      setBusy(false);
    }
  }

  async function submitReview(e) {
    e.preventDefault();
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await api("/api/reviews", {
        token: auth.token,
        method: "POST",
        body: { reservationId: reviewFor, rating: Number(rating), comment },
      });
      setNotice("Thank you, your review was saved.");
      setReviewFor(null);
      setRating(5);
      setComment("");
      await load();
    } catch (e2) {
      if (e2.status === 401) onUnauthorized();
      else setError(e2.message);
    } finally {
      setBusy(false);
    }
  }

  if (loading) return <p className="dashMuted">Loading your bookings...</p>;

  return (
    <div className="dash">
      {error && <p className="dashError">{error}</p>}
      {notice && <p className="dashNotice">{notice}</p>}

      {summary && (
        <div className="statGrid">
          <Stat label="Upcoming" value={summary.upcoming} />
          <Stat label="Past stays" value={summary.past} />
          <Stat label="Awaiting payment" value={summary.pending} />
          <Stat label="Cancelled / expired" value={summary.cancelled} />
          <Stat label="Total spent" value={pkr(summary.totalSpent)} />
          <Stat label="Reviews written" value={summary.reviewsWritten} />
        </div>
      )}

      <h2 className="dashH2">My bookings</h2>

      {bookings.length === 0 ? (
        <p className="dashMuted">
          No bookings yet. Go to Chat and ask the assistant to find you a room.
        </p>
      ) : (
        <div className="cardList">
          {bookings.map((b) => (
            <div key={b.id} className="bookCard">
              <div className="bookTop">
                <strong>
                  {b.hotelName ?? "Hotel"} · Room {b.roomNumber}
                </strong>
                <span className={`badge badge-${b.status}`}>{b.status}</span>
              </div>
              <p className="dashMuted">
                {b.city ? `${b.city} · ` : ""}
                {b.checkIn} → {b.checkOut} · {b.guests} guest(s)
              </p>
              <p>
                Total: <strong>{pkr(b.totalPrice)}</strong> · Booking ID {b.id}
                {b.paidAt ? " · Paid" : ""}
              </p>

              <div className="bookActions">
                {b.canCancel && (
                  <button disabled={busy} onClick={() => cancel(b.id)}>
                    Cancel booking
                  </button>
                )}
                {b.canReview && (
                  <button disabled={busy} onClick={() => setReviewFor(b.id)}>
                    Write a review
                  </button>
                )}
                {b.reviewed && <span className="dashMuted">Reviewed ✓</span>}
              </div>

              {reviewFor === b.id && (
                <form className="reviewForm" onSubmit={submitReview}>
                  <label>
                    Rating
                    <select value={rating} onChange={(e) => setRating(e.target.value)}>
                      {[5, 4, 3, 2, 1].map((n) => (
                        <option key={n} value={n}>
                          {n} {"★".repeat(n)}
                        </option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Comment (optional)
                    <textarea
                      value={comment}
                      onChange={(e) => setComment(e.target.value)}
                      maxLength={1000}
                      rows={3}
                    />
                  </label>
                  <div className="bookActions">
                    <button type="submit" disabled={busy}>
                      Submit review
                    </button>
                    <button type="button" onClick={() => setReviewFor(null)}>
                      Close
                    </button>
                  </div>
                </form>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Stat({ label, value }) {
  return (
    <div className="statCard">
      <span className="statValue">{value}</span>
      <span className="statLabel">{label}</span>
    </div>
  );
}