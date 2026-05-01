import { useState } from 'react';
import { api } from '../api';

type Msg = { kind: 'ok' | 'err'; text: string };

export function IngestView() {
  const [topic, setTopic] = useState('');
  const [topicMsg, setTopicMsg] = useState<Msg | null>(null);
  const [topicLoading, setTopicLoading] = useState(false);

  const [textId, setTextId] = useState('');
  const [textBody, setTextBody] = useState('');
  const [textTags, setTextTags] = useState('');
  const [textMsg, setTextMsg] = useState<Msg | null>(null);
  const [textLoading, setTextLoading] = useState(false);

  async function ingestTopic(e: React.FormEvent) {
    e.preventDefault();
    if (!topic.trim()) return;
    setTopicLoading(true);
    setTopicMsg(null);
    try {
      const r = await api.ingestOrigamTopic(topic.trim());
      setTopicMsg({ kind: 'ok', text: `Ingested "${r.title}" (${r.posts} posts, ${r.chars} chars) → ${r.docId}` });
      setTopic('');
    } catch (e: any) {
      setTopicMsg({ kind: 'err', text: e.message ?? String(e) });
    } finally {
      setTopicLoading(false);
    }
  }

  async function ingestText(e: React.FormEvent) {
    e.preventDefault();
    if (!textId.trim() || !textBody.trim()) return;
    setTextLoading(true);
    setTextMsg(null);
    try {
      const tags: Record<string, string> = {};
      for (const line of textTags.split('\n')) {
        const [k, ...rest] = line.split('=');
        if (k && rest.length) tags[k.trim()] = rest.join('=').trim();
      }
      const r = await api.ingestText(textId.trim(), textBody, Object.keys(tags).length ? tags : undefined);
      setTextMsg({ kind: 'ok', text: `Ingested as ${r.documentId}` });
      setTextId('');
      setTextBody('');
      setTextTags('');
    } catch (e: any) {
      setTextMsg({ kind: 'err', text: e.message ?? String(e) });
    } finally {
      setTextLoading(false);
    }
  }

  return (
    <div className="grid md:grid-cols-2 gap-4">
      <form onSubmit={ingestTopic} className="rounded-lg border border-zinc-800 bg-zinc-900/50 p-4 space-y-3">
        <h3 className="font-medium text-zinc-100">Ingest ORIGAM community topic</h3>
        <p className="text-xs text-zinc-500">Paste a topic ID (e.g. <code>3932</code>) or a full URL.</p>
        <input
          value={topic}
          onChange={e => setTopic(e.target.value)}
          placeholder="3932 or https://community.origam.com/t/.../3932"
          className="w-full bg-zinc-950 border border-zinc-800 rounded px-3 py-2 text-sm text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <button
          type="submit"
          disabled={topicLoading || !topic.trim()}
          className="px-4 py-2 rounded bg-purple-600 hover:bg-purple-500 disabled:opacity-40 text-sm font-medium"
        >
          {topicLoading ? 'Ingesting…' : 'Ingest topic'}
        </button>
        {topicMsg && <Status msg={topicMsg} />}
      </form>

      <form onSubmit={ingestText} className="rounded-lg border border-zinc-800 bg-zinc-900/50 p-4 space-y-3">
        <h3 className="font-medium text-zinc-100">Ingest plain text</h3>
        <input
          value={textId}
          onChange={e => setTextId(e.target.value)}
          placeholder="Document ID (e.g. nastya-bio)"
          className="w-full bg-zinc-950 border border-zinc-800 rounded px-3 py-2 text-sm text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <textarea
          value={textBody}
          onChange={e => setTextBody(e.target.value)}
          placeholder="Text content…"
          rows={5}
          className="w-full bg-zinc-950 border border-zinc-800 rounded px-3 py-2 text-sm text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <textarea
          value={textTags}
          onChange={e => setTextTags(e.target.value)}
          placeholder={'Optional tags, one per line:\nkey=value'}
          rows={2}
          className="w-full bg-zinc-950 border border-zinc-800 rounded px-3 py-2 text-xs font-mono text-zinc-100 focus:outline-none focus:border-purple-500"
        />
        <button
          type="submit"
          disabled={textLoading || !textId.trim() || !textBody.trim()}
          className="px-4 py-2 rounded bg-purple-600 hover:bg-purple-500 disabled:opacity-40 text-sm font-medium"
        >
          {textLoading ? 'Ingesting…' : 'Ingest text'}
        </button>
        {textMsg && <Status msg={textMsg} />}
      </form>
    </div>
  );
}

function Status({ msg }: { msg: Msg }) {
  return (
    <div
      className={`text-sm rounded p-2 border ${
        msg.kind === 'ok'
          ? 'border-emerald-700 bg-emerald-950/40 text-emerald-200'
          : 'border-red-700 bg-red-950/40 text-red-200'
      }`}
    >
      {msg.text}
    </div>
  );
}
