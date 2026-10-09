import { useState, useRef, useEffect, useMemo, useCallback } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import AuthPage from "./AuthPage";
import MyBookings from "./MyBookings";
import AdminDashboard from "./AdminDashboard";
import Hotels from "./Hotels";
import "./App.css";
import "./Dashboard.css";

const API_BASE = import.meta.env.VITE_API_URL ?? "https://localhost:7115";
const API_URL = `${API_BASE}/api/chat`;
const STORAGE_KEY = "staygenie_conversations";
const AUTH_KEY = "staygenie_auth";

const WELCOME =
  "Welcome to StayGenie. Tell me your dates, how many guests, and what you'd like in a room, and I'll find what's free and book it for you.";

const SUGGESTIONS = [
  "Sea view room for 2, 10 to 12 Nov, under 20000",
  "Family room for 4 with breakfast",
  "Cheapest room for 2 next weekend",
  "Luxury suite with jacuzzi",
];

// Chat ke links (jaise payment link) nayi tab mein khulein, taake chat band na ho
const markdownComponents = {
  a: ({ href, children }) => (
    <a href={href} target="_blank" rel="noopener noreferrer">
      {children}
    </a>
  ),
};

function newConversation() {
  return {
    id: crypto.randomUUID(),
    title: "New chat",
    createdAt: Date.now(),
    messages: [{ role: "assistant", content: WELCOME }],
  };
}

function loadConversations() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (Array.isArray(parsed) && parsed.length) return parsed;
    }
  } catch {
    /* ignore */
  }
  return [newConversation()];
}

function loadAuth() {
  try {
    const raw = localStorage.getItem(AUTH_KEY);
    if (raw) {
      const parsed = JSON.parse(raw);
      if (parsed?.token) return parsed;
    }
  } catch {
    /* ignore */
  }
  return null;
}

function groupLabel(ts) {
  const d = new Date(ts);
  const now = new Date();
  const days = Math.floor((now.setHours(0, 0, 0, 0) - new Date(d).setHours(0, 0, 0, 0)) / 86400000);
  if (days <= 0) return "Today";
  if (days === 1) return "Yesterday";
  if (days < 7) return "Previous 7 days";
  if (days < 30) return "Previous 30 days";
  return "Older";
}

export default function App() {
  const [conversations, setConversations] = useState(loadConversations);
  const [activeId, setActiveId] = useState(() => loadConversations()[0].id);
  const [input, setInput] = useState("");
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState("");
  const [auth, setAuth] = useState(loadAuth); // { token, name, role } ya null
  const [showAuth, setShowAuth] = useState(false);
  const [view, setView] = useState("chat"); // "chat" | "hotels" | "bookings" | "admin"
  // Adyen payment ke baad wapas aane par (?payment=done) ek message dikhao
  const [payNotice, setPayNotice] = useState(
    () => new URLSearchParams(window.location.search).get("payment") === "done"
  );
  const bottomRef = useRef(null);
  const inputRef = useRef(null);

  const active = conversations.find((c) => c.id === activeId) ?? conversations[0];

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(conversations));
  }, [conversations]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [active?.messages.length, loading, activeId, view]);

  // Address bar saaf karo, taake refresh par message dobara na aaye
  useEffect(() => {
    if (payNotice) window.history.replaceState({}, "", window.location.pathname);
  }, [payNotice]);

  function updateActive(updater) {
    setConversations((prev) => prev.map((c) => (c.id === activeId ? updater(c) : c)));
  }

  function handleAuthSuccess(user) {
    localStorage.setItem(AUTH_KEY, JSON.stringify(user));
    setAuth(user);
    setShowAuth(false);
  }

  // Sign out (ya token expire / 401): login hatao aur chat par wapas
  const signOut = useCallback(() => {
    localStorage.removeItem(AUTH_KEY);
    setAuth(null);
    setView("chat");
  }, []);

  async function send(text) {
    const value = (text ?? input).trim();
    if (!value || loading) return;

    const userMsg = { role: "user", content: value };
    updateActive((c) => ({
      ...c,
      messages: [...c.messages, userMsg],
      title: c.title === "New chat" ? value.slice(0, 48) : c.title,
    }));
    setInput("");
    setLoading(true);

    const history = [...active.messages, userMsg].map(({ role, content }) => ({ role, content }));

    try {
      const headers = { "Content-Type": "application/json" };
      if (auth?.token) headers.Authorization = `Bearer ${auth.token}`;

      const res = await fetch(API_URL, {
        method: "POST",
        headers,
        body: JSON.stringify({ messages: history }),
      });
      if (!res.ok) throw new Error(String(res.status));
      const data = await res.json();
      updateActive((c) => ({ ...c, messages: [...c.messages, { role: "assistant", content: data.reply }] }));
    } catch {
      updateActive((c) => ({
        ...c,
        messages: [
          ...c.messages,
          {
            role: "assistant",
            error: true,
            content: "I couldn't reach the booking service. Check that the API is running and try again.",
          },
        ],
      }));
    } finally {
      setLoading(false);
      inputRef.current?.focus();
    }
  }

  function startNewChat() {
    const conv = newConversation();
    setConversations((prev) => [conv, ...prev]);
    setActiveId(conv.id);
    setInput("");
    setView("chat");
    inputRef.current?.focus();
  }

  // Hotels page se: chat mein jao aur message pehle se likha hua milay
  function askAssistant(text) {
    setInput(text);
    setView("chat");
    setTimeout(() => inputRef.current?.focus(), 0);
  }

  function deleteConversation(id, e) {
    e.stopPropagation();
    setConversations((prev) => {
      const next = prev.filter((c) => c.id !== id);
      if (next.length === 0) {
        const conv = newConversation();
        setActiveId(conv.id);
        return [conv];
      }
      if (id === activeId) setActiveId(next[0].id);
      return next;
    });
  }

  const grouped = useMemo(() => {
    const filtered = conversations.filter((c) =>
      c.title.toLowerCase().includes(query.trim().toLowerCase())
    );
    const groups = {};
    for (const c of filtered) {
      const label = groupLabel(c.createdAt);
      (groups[label] ??= []).push(c);
    }
    return groups;
  }, [conversations, query]);

  const groupOrder = ["Today", "Yesterday", "Previous 7 days", "Previous 30 days", "Older"];
  const fresh = active?.messages.length === 1;
  const isAdmin = auth?.role === "Admin";

  // Kaun sa page dikhana hai
  const showHotels = view === "hotels";
  const showBookings = view === "bookings" && !!auth;
  const showAdmin = view === "admin" && isAdmin;
  const showChat = !showHotels && !showBookings && !showAdmin;

  return (
    <div className="app">
      <aside className="side">
        <div className="side-top">
          <div className="brand">
            <span className="mark" aria-hidden="true">✦</span>
            <span className="name">StayGenie</span>
          </div>
          <button className="newChatBtn" onClick={startNewChat}>
            <span className="plus" aria-hidden="true">+</span> New chat
          </button>

          <div className="navBtns">
            <button
              className={"navBtn" + (showChat ? " active" : "")}
              onClick={() => setView("chat")}
            >
              Chat
            </button>
            <button
              className={"navBtn" + (showHotels ? " active" : "")}
              onClick={() => setView("hotels")}
            >
              Hotels
            </button>
            {auth && (
              <button
                className={"navBtn" + (showBookings ? " active" : "")}
                onClick={() => setView("bookings")}
              >
                My bookings
              </button>
            )}
            {isAdmin && (
              <button
                className={"navBtn" + (showAdmin ? " active" : "")}
                onClick={() => setView("admin")}
              >
                Admin dashboard
              </button>
            )}
          </div>

          <div className="search">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <circle cx="11" cy="11" r="7" stroke="currentColor" strokeWidth="2" />
              <path d="M21 21l-4.3-4.3" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
            </svg>
            <input
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search chats"
              aria-label="Search chats"
            />
          </div>
        </div>

        <nav className="history">
          {groupOrder
            .filter((g) => grouped[g]?.length)
            .map((label) => (
              <div key={label} className="group">
                <h3>{label}</h3>
                {grouped[label].map((c) => (
                  <button
                    key={c.id}
                    className={"histItem" + (c.id === activeId ? " active" : "")}
                    onClick={() => {
                      setActiveId(c.id);
                      setView("chat");
                    }}
                  >
                    <span className="histTitle">{c.title}</span>
                    <span className="histDelete" onClick={(e) => deleteConversation(c.id, e)} title="Delete">
                      ✕
                    </span>
                  </button>
                ))}
              </div>
            ))}
          {conversations.length > 0 && Object.keys(grouped).length === 0 && (
            <p className="noResults">No chats match "{query}"</p>
          )}
        </nav>

        <div className="side-foot">
          <div className="authFoot">
            {auth ? (
              <>
                <p className="authUser">
                  Signed in as <strong>{auth.name}</strong> ({auth.role})
                </p>
                <button onClick={signOut}>Sign out</button>
              </>
            ) : (
              <button onClick={() => setShowAuth(true)}>Sign in / Register</button>
            )}
          </div>
          <p>Live availability checked on every booking.</p>
        </div>
      </aside>

      {showHotels && (
        <main className="main">
          <header className="top">
            <h1>Hotels</h1>
          </header>
          <div className="pageBody">
            <Hotels onAskAssistant={askAssistant} />
          </div>
        </main>
      )}

      {showBookings && (
        <main className="main">
          <header className="top">
            <h1>My bookings</h1>
          </header>
          <div className="pageBody">
            <MyBookings auth={auth} onUnauthorized={signOut} />
          </div>
        </main>
      )}

      {showAdmin && (
        <main className="main">
          <header className="top">
            <h1>Admin dashboard</h1>
          </header>
          <div className="pageBody">
            <AdminDashboard auth={auth} onUnauthorized={signOut} />
          </div>
        </main>
      )}

      {showChat && (
        <main className="main">
          <header className="top">
            <h1>{active?.title === "New chat" ? "Reservation assistant" : active?.title}</h1>
          </header>

          {payNotice && (
            <div
              role="status"
              style={{
                margin: "12px auto 0",
                maxWidth: 720,
                padding: "10px 14px",
                borderRadius: 8,
                background: "#e8f5e9",
                color: "#1b5e20",
                fontSize: 14,
                display: "flex",
                justifyContent: "space-between",
                gap: 12,
              }}
            >
              <span>
                Thanks for completing the payment step. If the payment went through, your booking will be
                confirmed shortly. Ask the assistant for your booking status with your reservation ID and name.
              </span>
              <button
                onClick={() => setPayNotice(false)}
                aria-label="Dismiss"
                style={{ background: "none", border: "none", cursor: "pointer", color: "inherit" }}
              >
                ✕
              </button>
            </div>
          )}

          <div className="stream">
            <div className="lane">
              {fresh && (
                <div className="hero">
                  <h2>Good evening. Ready to find a room?</h2>
                  <p>Tell me a few details, and I'll take it from there.</p>
                  <div className="suggestions">
                    {SUGGESTIONS.map((s) => (
                      <button key={s} className="suggestion" onClick={() => send(s)} disabled={loading}>
                        {s}
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {!fresh &&
                active.messages.map((m, i) => (
                  <div key={i} className={"turn " + m.role}>
                    <div className="turnInner">
                      {m.role === "assistant" && (
                        <span className="who asst" aria-hidden="true">✦</span>
                      )}
                      <div className={"content" + (m.error ? " error" : "")}>
                        {m.role === "assistant" ? (
                          <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents}>
                            {m.content}
                          </ReactMarkdown>
                        ) : (
                          <p>{m.content}</p>
                        )}
                      </div>
                    </div>
                  </div>
                ))}

              {loading && (
                <div className="turn assistant">
                  <div className="turnInner">
                    <span className="who asst" aria-hidden="true">✦</span>
                    <div className="content">
                      <span className="typing" aria-label="StayGenie is typing">
                        <i /><i /><i />
                      </span>
                    </div>
                  </div>
                </div>
              )}
              <div ref={bottomRef} />
            </div>
          </div>

          <div className="composerWrap">
            <form
              className="composer"
              onSubmit={(e) => {
                e.preventDefault();
                send();
              }}
            >
              <input
                ref={inputRef}
                value={input}
                onChange={(e) => setInput(e.target.value)}
                placeholder="Message StayGenie"
                aria-label="Message"
              />
              <button type="submit" disabled={loading || !input.trim()} aria-label="Send">
                <svg width="17" height="17" viewBox="0 0 24 24" fill="none" aria-hidden="true">
                  <path d="M5 12h14M13 6l6 6-6 6" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
                </svg>
              </button>
            </form>
            <p className="disclaimer">StayGenie checks live availability before confirming any booking.</p>
          </div>
        </main>
      )}

      {showAuth && (
        <AuthPage apiBase={API_BASE} onClose={() => setShowAuth(false)} onSuccess={handleAuthSuccess} />
      )}
    </div>
  );
}