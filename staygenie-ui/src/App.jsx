import { useState, useRef, useEffect, useMemo } from "react";
import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";
import "./App.css";

const API_URL = "https://localhost:7115/api/chat";
const STORAGE_KEY = "staygenie_conversations";

const WELCOME =
  "Welcome to StayGenie. Tell me your dates, how many guests, and what you'd like in a room, and I'll find what's free and book it for you.";

const SUGGESTIONS = [
  "Sea view room for 2, 10 to 12 Nov, under 20000",
  "Family room for 4 with breakfast",
  "Cheapest room for 2 next weekend",
  "Luxury suite with jacuzzi",
];

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
  const bottomRef = useRef(null);
  const inputRef = useRef(null);

  const active = conversations.find((c) => c.id === activeId) ?? conversations[0];

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(conversations));
  }, [conversations]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: "end" });
  }, [active?.messages.length, loading, activeId]);

  function updateActive(updater) {
    setConversations((prev) => prev.map((c) => (c.id === activeId ? updater(c) : c)));
  }

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
      const res = await fetch(API_URL, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
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
    inputRef.current?.focus();
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
                    onClick={() => setActiveId(c.id)}
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
          <p>Live availability checked on every booking.</p>
        </div>
      </aside>

      <main className="main">
        <header className="top">
          <h1>{active?.title === "New chat" ? "Reservation assistant" : active?.title}</h1>
        </header>

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
                        <ReactMarkdown remarkPlugins={[remarkGfm]}>{m.content}</ReactMarkdown>
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
    </div>
  );
}