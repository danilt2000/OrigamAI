import { useEffect, useState } from 'react';
import ReactMarkdown from 'react-markdown';
import remarkGfm from 'remark-gfm';
import type { Message } from '../db';
import { PipelinePanel } from './PipelinePanel';
import { lightbox } from './ImageLightbox';

export function MessageBubble({ message }: { message: Message }) {
  const isUser = message.role === 'user';
  const [imgUrls, setImgUrls] = useState<string[]>([]);

  useEffect(() => {
    if (!message.images || message.images.length === 0) {
      setImgUrls([]);
      return;
    }
    const urls = message.images.map(i => URL.createObjectURL(i.blob));
    setImgUrls(urls);
    return () => urls.forEach(URL.revokeObjectURL);
  }, [message.images]);

  return (
    <div className={`flex fade-in ${isUser ? 'justify-end' : 'justify-start'}`}>
      <div
        className={`max-w-[85%] rounded-2xl px-4 py-3 ${
          isUser
            ? 'bg-purple-600/30 border border-purple-500/40 text-zinc-50'
            : 'bg-zinc-900 border border-zinc-800 text-zinc-100'
        }`}
      >
        {imgUrls.length > 0 && (
          <div className="flex gap-2 flex-wrap mb-2">
            {imgUrls.map((u, i) => (
              <button
                key={i}
                type="button"
                onClick={() => lightbox.open(imgUrls, i)}
                className="block p-0 border-0 bg-transparent cursor-zoom-in"
                title="Click to zoom"
              >
                <img src={u} className="max-h-48 rounded-lg border border-zinc-700 hover:border-purple-500/60 transition-colors" />
              </button>
            ))}
          </div>
        )}
        {message.text && (
          <div className="markdown-body">
            <ReactMarkdown remarkPlugins={[remarkGfm]}>{message.text}</ReactMarkdown>
          </div>
        )}
        {message.sources && message.sources.length > 0 && (
          <div className="mt-3 pt-3 border-t border-zinc-800/80">
            <div className="text-[10px] uppercase tracking-wider text-zinc-500 mb-1.5">Sources</div>
            <ul className="space-y-1">
              {message.sources.map((s, i) => (
                <li key={i} className="text-xs flex items-center gap-2">
                  <span className="text-zinc-500 tabular-nums w-9">{s.relevance.toFixed(2)}</span>
                  {s.url ? (
                    <a href={s.url} target="_blank" rel="noreferrer" className="text-purple-400 hover:text-purple-300 truncate">
                      {s.title || s.documentId}
                    </a>
                  ) : (
                    <span className="truncate">{s.title || s.documentId}</span>
                  )}
                </li>
              ))}
            </ul>
          </div>
        )}
        {!isUser && message.pipeline && <PipelinePanel pipeline={message.pipeline} />}
      </div>
    </div>
  );
}
