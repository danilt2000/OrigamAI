import { openDB, type DBSchema, type IDBPDatabase } from 'idb';
import type { AskSource, Pipeline } from './api';

export type ChatImage = { blob: Blob; mime: string };

export type Message = {
  id: string;
  chatId: string;
  role: 'user' | 'assistant';
  text: string;
  images?: ChatImage[];
  sources?: AskSource[];
  imageCaption?: string | null;
  searchQuery?: string | null;
  pipeline?: Pipeline | null;
  createdAt: number;
};

export type Chat = {
  id: string;
  title: string;
  filter?: string;
  createdAt: number;
  updatedAt: number;
};

interface OrigamDB extends DBSchema {
  chats: {
    key: string;
    value: Chat;
    indexes: { 'by-updated': number };
  };
  messages: {
    key: string;
    value: Message;
    indexes: { 'by-chat': string };
  };
}

let dbp: Promise<IDBPDatabase<OrigamDB>> | null = null;

function getDb() {
  if (!dbp) {
    dbp = openDB<OrigamDB>('origam-ai', 1, {
      upgrade(db) {
        const chats = db.createObjectStore('chats', { keyPath: 'id' });
        chats.createIndex('by-updated', 'updatedAt');
        const messages = db.createObjectStore('messages', { keyPath: 'id' });
        messages.createIndex('by-chat', 'chatId');
      },
    });
  }
  return dbp;
}

const uuid = () =>
  (crypto as any).randomUUID
    ? (crypto as any).randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

export const chatStore = {
  async list(): Promise<Chat[]> {
    const db = await getDb();
    const all = await db.getAllFromIndex('chats', 'by-updated');
    return all.reverse();
  },
  async create(title = 'New chat'): Promise<Chat> {
    const db = await getDb();
    const now = Date.now();
    const chat: Chat = { id: uuid(), title, createdAt: now, updatedAt: now };
    await db.put('chats', chat);
    return chat;
  },
  async update(id: string, patch: Partial<Chat>): Promise<Chat | null> {
    const db = await getDb();
    const existing = await db.get('chats', id);
    if (!existing) return null;
    const updated = { ...existing, ...patch, updatedAt: Date.now() };
    await db.put('chats', updated);
    return updated;
  },
  async remove(id: string): Promise<void> {
    const db = await getDb();
    const tx = db.transaction(['chats', 'messages'], 'readwrite');
    await tx.objectStore('chats').delete(id);
    const idx = tx.objectStore('messages').index('by-chat');
    let cursor = await idx.openCursor(IDBKeyRange.only(id));
    while (cursor) {
      await cursor.delete();
      cursor = await cursor.continue();
    }
    await tx.done;
  },
  async get(id: string): Promise<Chat | undefined> {
    const db = await getDb();
    return db.get('chats', id);
  },
};

export const messageStore = {
  async listForChat(chatId: string): Promise<Message[]> {
    const db = await getDb();
    const items = await db.getAllFromIndex('messages', 'by-chat', chatId);
    return items.sort((a, b) => a.createdAt - b.createdAt);
  },
  async add(msg: Omit<Message, 'id' | 'createdAt'>): Promise<Message> {
    const db = await getDb();
    const m: Message = { ...msg, id: uuid(), createdAt: Date.now() };
    await db.put('messages', m);
    await chatStore.update(msg.chatId, {});
    return m;
  },
  async remove(id: string): Promise<void> {
    const db = await getDb();
    await db.delete('messages', id);
  },
};
