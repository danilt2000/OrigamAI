import { useEffect, useRef, useState } from 'react';
import { api, type AskHistoryItem } from '../api';
import { chatStore, messageStore, type Chat, type Message } from '../db';
import { ChatSidebar } from './ChatSidebar';
import { Composer } from './Composer';
import { LoadingBubble } from './LoadingBubble';
import { MessageBubble } from './MessageBubble';

export function ChatView() {
  const [chats, setChats] = useState<Chat[]>([]);
  const [currentId, setCurrentId] = useState<string | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [sending, setSending] = useState(false);
  const [sendingHasImages, setSendingHasImages] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [filterDraft, setFilterDraft] = useState('');
  const scrollRef = useRef<HTMLDivElement>(null);
  const sendingRef = useRef(false);

  const current = chats.find(c => c.id === currentId) ?? null;

  useEffect(() => {
    chatStore.list().then(setChats);
  }, []);

  useEffect(() => {
    if (!currentId) {
      setMessages([]);
      setFilterDraft('');
      return;
    }
    messageStore.listForChat(currentId).then(setMessages);
    chatStore.get(currentId).then(c => setFilterDraft(c?.filter ?? ''));
  }, [currentId]);

  useEffect(() => {
    scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
  }, [messages, sending]);

  async function refreshChats() {
    setChats(await chatStore.list());
  }

  async function newChat() {
    const c = await chatStore.create();
    await refreshChats();
    setCurrentId(c.id);
  }

  async function renameChat(id: string, title: string) {
    await chatStore.update(id, { title });
    await refreshChats();
  }

  async function deleteChat(id: string) {
    await chatStore.remove(id);
    if (currentId === id) setCurrentId(null);
    await refreshChats();
  }

  async function applyFilter() {
    if (!currentId) return;
    await chatStore.update(currentId, { filter: filterDraft.trim() || undefined });
    await refreshChats();
  }

  async function send({ text, images }: { text: string; images: Blob[] }) {
    if (sendingRef.current) return; // prevent reentry / accidental double-fire
    sendingRef.current = true;
    setErr(null);
    try {
      let chat = current;
      if (!chat) {
        chat = await chatStore.create(text ? text.slice(0, 60) : 'New chat');
        await refreshChats();
        setCurrentId(chat.id);
      } else if (chat.title === 'New chat' && text) {
        await chatStore.update(chat.id, { title: text.slice(0, 60) });
        await refreshChats();
      }

      // Snapshot prior history (last 10 text messages, before this turn) for the LLM.
      const priorMessages = await messageStore.listForChat(chat.id);
      const history: AskHistoryItem[] = priorMessages
        .filter(m => m.text && m.text.trim().length > 0)
        .slice(-10)
        .map(m => ({ role: m.role, content: m.text }));

      await messageStore.add({
        chatId: chat.id,
        role: 'user',
        text,
        images: images.map(b => ({ blob: b, mime: b.type || 'image/png' })),
      });
      setMessages(await messageStore.listForChat(chat.id));

      setSending(true);
      setSendingHasImages(images.length > 0);
      try {
        const res = images.length > 0
          ? await api.askWithImages(text, chat.filter, images, history)
          : await api.ask(text, chat.filter, history);

        await messageStore.add({
          chatId: chat.id,
          role: 'assistant',
          text: res.answer,
          sources: res.sources,
          imageCaption: res.imageCaption ?? null,
          searchQuery: res.searchQuery ?? null,
          pipeline: res.pipeline ?? null,
        });
        setMessages(await messageStore.listForChat(chat.id));
      } catch (e: any) {
        setErr(e.message ?? String(e));
      } finally {
        setSending(false);
        setSendingHasImages(false);
      }
    } finally {
      sendingRef.current = false;
    }
  }

  return (
    <div className="flex h-[calc(100vh-160px)] border border-zinc-800 rounded-xl overflow-hidden">
      <ChatSidebar
        chats={chats}
        currentId={currentId}
        onSelect={setCurrentId}
        onNew={newChat}
        onRename={renameChat}
        onDelete={deleteChat}
      />
      <div className="flex-1 flex flex-col min-w-0">
        {current && (
          <div className="flex items-center gap-2 px-4 py-2 border-b border-zinc-800 bg-zinc-900/40">
            <div className="text-sm text-zinc-300 truncate flex-1">{current.title}</div>
            <input
              value={filterDraft}
              onChange={e => setFilterDraft(e.target.value)}
              onBlur={applyFilter}
              onKeyDown={e => e.key === 'Enter' && (e.target as HTMLInputElement).blur()}
              placeholder="Tag filter (e.g. source=origam-community)"
              className="w-72 bg-zinc-950 border border-zinc-800 rounded px-2 py-1 text-xs text-zinc-200 focus:outline-none focus:border-purple-500"
            />
          </div>
        )}

        <div ref={scrollRef} className="flex-1 overflow-y-auto p-4 space-y-3">
          {!current && (
            <div className="h-full flex items-center justify-center text-zinc-500 text-sm">
              Pick a chat from the sidebar or start a new one.
            </div>
          )}
          {current && messages.length === 0 && !sending && (
            <div className="h-full flex items-center justify-center text-zinc-500 text-sm">
              Send your first message to start the conversation.
            </div>
          )}
          {messages.map(m => (
            <MessageBubble key={m.id} message={m} />
          ))}
          {sending && <LoadingBubble hasImages={sendingHasImages} />}
          {err && (
            <div className="rounded-lg border border-red-700 bg-red-950/50 p-3 text-sm text-red-200">{err}</div>
          )}
        </div>

        <div className="p-3 border-t border-zinc-800">
          <Composer onSubmit={send} disabled={sending} />
        </div>
      </div>
    </div>
  );
}
