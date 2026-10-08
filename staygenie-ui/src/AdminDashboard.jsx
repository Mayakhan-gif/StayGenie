import { useEffect, useState, useCallback } from "react";
import { api } from "./api";
import "./Dashboard.css";

const pkr = (n) => `PKR ${Number(n ?? 0).toLocaleString()}`;
const TABS = ["Bookings", "Customers", "Reviews"];

export default function AdminDashboard({ auth, onUnauthorized }) {
  const [stats, setStats] = useState(null);
  const [tab, setTab] = useState("Bookings");
  const [rows, setRows] = useState([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  const handle = useCallback(
    (e) => {
      if (e.status === 401) onUnauthorized();
      else setError(e.message);
    },
    [onUnauthorized]
  );

  useEffect(() => {
    api("/api/admin/stats", { token: auth.token })
      .then(setStats)
      .catch(handle);
  }, [auth.token, handle]);

  const loadTab = useCallback(async () => {
    setLoading(true);
    setError("");
    const path = {
      Bookings: "/api/admin/reservations",
      Customers: "/api/admin/customers",
      Reviews: "/api/admin/reviews",
    }[tab];
    try {
      setRows(await api(path, { token: auth.token }));
    } catch (e) {
      handle(e);
    } finally {
      setLoading(false);
    }
  }, [tab, auth.token, handle]);

  useEffect(() => {
    loadTab();
  }, [loadTab]);

  async function deleteReview(id) {
    if (!window.confirm("Delete this review?")) return;
    try {
      await api(`/api/reviews/${id}`, { token: auth.token, method: "DELETE" });
      await loadTab();
    } catch (e) {
      handle(e);
    }
  }

  return (
    <div className="dash">
      {error && <p className="dashError">{error}</p>}

      {stats && (
        <div className="statGrid">
          <Stat label="Hotels" value={stats.totalHotels} />
          <Stat label="Rooms" value={stats.totalRooms} />
          <Stat label="Customers" value={stats.totalCustomers} />
          <Stat label="Total bookings" value={stats.totalBookings} />
          <Stat label="Today's bookings" value={stats.todaysBookings} />
          <Stat label="Confirmed" value={stats.confirmed} />
          <Stat label="Pending payment" value={stats.pending} />
          <Stat label="Cancelled" value={stats.cancelled} />
          <Stat label="Expired" value={stats.expired} />
          <Stat label="Revenue" value={pkr(stats.revenue)} />
          <Stat label="Occupied tonight" value={`${stats.occupiedRoomsTonight} (${stats.occupancyPercent}%)`} />
        </div>
      )}

      <div className="tabs">
        {TABS.map((t) => (
          <button key={t} className={t === tab ? "tab active" : "tab"} onClick={() => setTab(t)}>
            {t}
          </button>
        ))}
      </div>

      {loading ? (
        <p className="dashMuted">Loading...</p>
      ) : rows.length === 0 ? (
        <p className="dashMuted">Nothing here yet.</p>
      ) : (
        <div className="tableWrap">
          {tab === "Bookings" && (
            <table>
              <thead>
                <tr>
                  <th>ID</th><th>Hotel / Room</th><th>Guest</th><th>Dates</th>
                  <th>Total</th><th>Status</th><th>Customer</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id}>
                    <td>{r.id}</td>
                    <td>{r.hotelName} · {r.roomNumber}</td>
                    <td>{r.guestName}</td>
                    <td>{r.checkIn} → {r.checkOut}</td>
                    <td>{pkr(r.totalPrice)}</td>
                    <td><span className={`badge badge-${r.status}`}>{r.status}</span></td>
                    <td>{r.customer ?? "-"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {tab === "Customers" && (
            <table>
              <thead>
                <tr><th>ID</th><th>Name</th><th>Email</th><th>Role</th><th>Bookings</th></tr>
              </thead>
              <tbody>
                {rows.map((u) => (
                  <tr key={u.id}>
                    <td>{u.id}</td><td>{u.name}</td><td>{u.email}</td>
                    <td>{u.role}</td><td>{u.bookings}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {tab === "Reviews" && (
            <table>
              <thead>
                <tr>
                  <th>ID</th><th>Hotel / Room</th><th>By</th><th>Rating</th><th>Comment</th><th></th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr key={r.id}>
                    <td>{r.id}</td>
                    <td>{r.hotelName} · {r.roomNumber}</td>
                    <td>{r.reviewer}</td>
                    <td>{"★".repeat(r.rating)}</td>
                    <td>{r.comment ?? "-"}</td>
                    <td><button onClick={() => deleteReview(r.id)}>Delete</button></td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
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