import { useEffect, useState } from "react";
import { api } from "./api";
import "./Hotels.css";

const pkr = (n) => `PKR ${Number(n ?? 0).toLocaleString()}`;

function Rating({ value, count }) {
  if (value == null) return <span className="dashMuted">No reviews yet</span>;
  return (
    <span className="rating">
      ★ {Number(value).toFixed(1)}
      {count != null && <span className="dashMuted"> ({count})</span>}
    </span>
  );
}

export default function Hotels({ onAskAssistant }) {
  const [hotels, setHotels] = useState([]);
  const [city, setCity] = useState("");
  const [selectedId, setSelectedId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    api("/api/hotels")
      .then(setHotels)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  if (selectedId) {
    return (
      <HotelDetail
        id={selectedId}
        onBack={() => setSelectedId(null)}
        onAskAssistant={onAskAssistant}
      />
    );
  }

  const cities = [...new Set(hotels.map((h) => h.city))].sort();
  const shown = city ? hotels.filter((h) => h.city === city) : hotels;

  if (loading) return <p className="dashMuted">Loading hotels...</p>;

  return (
    <div className="dash">
      {error && <p className="dashError">{error}</p>}

      <div className="hotelBar">
        <label>
          City{" "}
          <select value={city} onChange={(e) => setCity(e.target.value)}>
            <option value="">All cities</option>
            {cities.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
        </label>
        <span className="dashMuted">{shown.length} hotel(s)</span>
      </div>

      {shown.length === 0 ? (
        <p className="dashMuted">No hotels found.</p>
      ) : (
        <div className="cardList">
          {shown.map((h) => (
            <div key={h.id} className="bookCard">
              <div className="bookTop">
                <strong>{h.name}</strong>
                <Rating value={h.averageRating} count={h.reviewCount} />
              </div>
              <p className="dashMuted">
                {h.city} · {h.address}
              </p>
              {h.description && <p>{h.description}</p>}
              <p className="dashMuted">{h.roomCount} room(s) available to book</p>
              <div className="bookActions">
                <button onClick={() => setSelectedId(h.id)}>View rooms</button>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function HotelDetail({ id, onBack, onAskAssistant }) {
  const [hotel, setHotel] = useState(null);
  const [error, setError] = useState("");
  const [openRoom, setOpenRoom] = useState(null);
  const [reviews, setReviews] = useState({}); // roomId -> reviews ka data

  useEffect(() => {
    api(`/api/hotels/${id}`)
      .then(setHotel)
      .catch((e) => setError(e.message));
  }, [id]);

  async function toggleReviews(roomId) {
    if (openRoom === roomId) {
      setOpenRoom(null);
      return;
    }
    setOpenRoom(roomId);
    if (!reviews[roomId]) {
      try {
        const data = await api(`/api/reviews/room/${roomId}`);
        setReviews((prev) => ({ ...prev, [roomId]: data }));
      } catch (e) {
        setError(e.message);
      }
    }
  }

  if (error && !hotel) {
    return (
      <div className="dash">
        <p className="dashError">{error}</p>
        <div className="bookActions">
          <button onClick={onBack}>Back to hotels</button>
        </div>
      </div>
    );
  }
  if (!hotel) return <p className="dashMuted">Loading hotel...</p>;

  return (
    <div className="dash">
      <div className="bookActions">
        <button onClick={onBack}>← Back to hotels</button>
      </div>

      {error && <p className="dashError">{error}</p>}

      <h2 className="dashH2">{hotel.name}</h2>
      <p>
        <Rating value={hotel.averageRating} count={hotel.reviewCount} />
      </p>
      <p className="dashMuted">
        {hotel.city} · {hotel.address}
        {hotel.phone ? ` · ${hotel.phone}` : ""}
      </p>
      {hotel.description && <p>{hotel.description}</p>}
      {hotel.amenities && <p className="dashMuted">Amenities: {hotel.amenities}</p>}

      <h2 className="dashH2">Rooms</h2>
      {hotel.rooms.length === 0 ? (
        <p className="dashMuted">No rooms available right now.</p>
      ) : (
        <div className="roomGrid">
          {hotel.rooms.map((r) => (
            <div key={r.id} className="roomCard">
              <div className="bookTop">
                <strong>
                  Room {r.roomNumber} · {r.type}
                </strong>
                <Rating value={r.averageRating} />
              </div>
              <p>
                <strong>{pkr(r.pricePerNight)}</strong> / night · up to {r.maxOccupancy} guest(s)
              </p>
              {r.description && <p className="dashMuted">{r.description}</p>}
              {r.amenities && <p className="dashMuted">{r.amenities}</p>}

              <div className="bookActions">
                <button
                  onClick={() =>
                    onAskAssistant(
                      `I'd like to book Room ${r.roomNumber} (${r.type}) at ${hotel.name} in ${hotel.city}. Please check availability for me.`
                    )
                  }
                >
                  Book with assistant
                </button>
                <button onClick={() => toggleReviews(r.id)}>
                  {openRoom === r.id ? "Hide reviews" : "Reviews"}
                </button>
              </div>

              {openRoom === r.id && (
                <div>
                  {!reviews[r.id] ? (
                    <p className="dashMuted">Loading reviews...</p>
                  ) : reviews[r.id].count === 0 ? (
                    <p className="dashMuted">No reviews for this room yet.</p>
                  ) : (
                    reviews[r.id].reviews.map((rv) => (
                      <div key={rv.id} className="reviewItem">
                        <strong>{"★".repeat(rv.rating)}</strong> · {rv.reviewer} ·{" "}
                        <span className="dashMuted">
                          {new Date(rv.createdAt).toLocaleDateString()}
                        </span>
                        {rv.comment && <p>{rv.comment}</p>}
                      </div>
                    ))
                  )}
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}