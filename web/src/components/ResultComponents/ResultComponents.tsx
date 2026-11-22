import { useState } from 'react';
import { colorClasses, formatDate, getRiskColor, getVerdict } from './utils';

export function Badge({ children, color = 'gray' }) {
  const classes = colorClasses[color] || colorClasses.gray;

  return (
    <span
      className={`inline-flex items-center px-3 py-1 rounded-full border text-xs font-medium ${classes.badge}`}
    >
      {children}
    </span>
  );
}

// ==========================================
// PODSUMOWANIE
// ==========================================

export function Overview({ claims }) {
  const stats = {
    confirmed: claims.filter((c) => c.outcome?.verdict === 'Confirmed').length,

    refuted: claims.filter((c) => c.outcome?.verdict === 'Refuted').length,

    other: claims.filter(
      (c) => !['Confirmed', 'Refuted'].includes(c.outcome?.verdict),
    ).length,
  };

  const items = [
    {
      value: stats.confirmed,
      label: 'Potwierdzone',
      color: 'text-green-600',
      background: 'bg-green-50',
    },
    {
      value: stats.refuted,
      label: 'Obalone',
      color: 'text-red-600',
      background: 'bg-red-50',
    },
    {
      value: stats.other,
      label: 'Nierozstrzygnięte',
      color: 'text-amber-600',
      background: 'bg-amber-50',
    },
  ];

  return (
    <section className="bg-white border border-gray-200 rounded-2xl p-5 sm:p-6">
      <h2 className="font-semibold text-lg mb-5">Podsumowanie weryfikacji</h2>

      <div className="grid grid-cols-3 gap-3">
        {items.map((item) => (
          <div
            key={item.label}
            className={`${item.background} rounded-xl p-3 sm:p-5 text-center`}
          >
            <div className={`text-3xl sm:text-4xl font-semibold ${item.color}`}>
              {item.value}
            </div>

            <div className="text-xs sm:text-sm text-gray-600 mt-2">
              {item.label}
            </div>
          </div>
        ))}
      </div>

      <p className="text-xs text-gray-400 mt-4">
        Łącznie wykryto {claims.length} twierdzeń. Liczba nierozstrzygniętych
        obejmuje również inne werdykty niż potwierdzone i obalone.
      </p>
    </section>
  );
}

// ==========================================
// DOWODY
// ==========================================

export function EvidenceCard({ evidence }) {
  const color =
    evidence.stance === 'Supports'
      ? 'green'
      : evidence.stance === 'Refutes'
        ? 'red'
        : evidence.stance === 'Misleading'
          ? 'orange'
          : 'gray';

  return (
    <article className="border border-gray-200 rounded-xl p-4 space-y-3">
      <div className="flex flex-wrap justify-between items-center gap-2">
        <span className="text-sm font-semibold">
          {evidence.publisher || evidence.sourceName}
        </span>

        <Badge color={color}>
          {evidence.stanceLabel || evidence.stance || 'Kontekst'}
        </Badge>
      </div>

      <div>
        <p className="text-sm font-medium text-gray-800">
          {evidence.title || evidence.sourceName}
        </p>

        <p className="text-xs text-gray-400 mt-1">
          {evidence.sourceTypeLabel || 'Źródło'}
          {' · '}
          {formatDate(evidence.date)}
        </p>
      </div>

      {evidence.quote && (
        <blockquote className="border-l-2 border-blue-400 pl-3 text-sm text-gray-600 leading-relaxed">
          „{evidence.quote}”
        </blockquote>
      )}

      <div className="flex flex-wrap items-center justify-between gap-3">
        <span className="text-xs text-gray-500">
          Waga dowodu:{' '}
          {typeof evidence.weight === 'number'
            ? evidence.weight.toFixed(2)
            : 'brak danych'}
        </span>

        {evidence.url && (
          <a
            href={evidence.url}
            target="_blank"
            rel="noopener noreferrer"
            className="text-xs text-blue-600 hover:underline font-medium"
          >
            Otwórz źródło ↗
          </a>
        )}
      </div>
    </article>
  );
}

// ==========================================
// PORÓWNANIE ŹRÓDEŁ
// ==========================================

export function Comparison({ comparison }) {
  if (!comparison) return null;

  const groups = [
    {
      label: 'Potwierdzające',
      items: comparison.supporting,
      color: 'text-green-700',
    },
    {
      label: 'Zaprzeczające',
      items: comparison.refuting,
      color: 'text-red-700',
    },
    {
      label: 'Wprowadzające w błąd',
      items: comparison.misleading,
      color: 'text-orange-700',
    },
    {
      label: 'Kontekstowe',
      items: comparison.contextOnly,
      color: 'text-gray-600',
    },
  ];

  return (
    <div className="border border-gray-200 rounded-xl p-4 space-y-4">
      <div>
        <p className="font-medium text-sm">Porównanie źródeł</p>

        <p className="text-xs text-gray-500 mt-1">
          {comparison.agreementLabel || 'Brak informacji o zgodności'}
        </p>
      </div>

      <p className="text-sm text-gray-600 leading-relaxed">
        {comparison.summary}
      </p>

      <div className="grid sm:grid-cols-2 gap-4">
        {groups.map(
          (group) =>
            group.items?.length > 0 && (
              <div key={group.label}>
                <p className={`text-xs font-semibold mb-2 ${group.color}`}>
                  {group.label}
                </p>

                <ul className="space-y-1">
                  {group.items.map((source, index) => (
                    <li key={index} className="text-xs text-gray-600">
                      • {source}
                    </li>
                  ))}
                </ul>
              </div>
            ),
        )}
      </div>

      {comparison.conflicts?.length > 0 && (
        <div className="bg-amber-50 rounded-lg p-3">
          <p className="text-xs font-semibold text-amber-800 mb-2">
            Wykryte rozbieżności
          </p>

          {comparison.conflicts.map((conflict, index) => (
            <p key={index} className="text-xs text-amber-800">
              {typeof conflict === 'string'
                ? conflict
                : JSON.stringify(conflict)}
            </p>
          ))}
        </div>
      )}
    </div>
  );
}

// ==========================================
// KARTA TWIERDZENIA
// ==========================================

export function ClaimCard({ claim }) {
  const [expanded, setExpanded] = useState(false);
  const [showProcess, setShowProcess] = useState(false);

  const verdict = getVerdict(claim.outcome?.verdict);
  const colors = colorClasses[verdict.color];

  const evidence = claim.evidence || [];
  const context = claim.context || [];
  const unknowns = claim.unknowns || [];

  const includedEvidence = evidence.filter(
    (item) => item.stance !== 'Rejected',
  );

  return (
    <article className="bg-white border border-gray-200 rounded-2xl overflow-hidden">
      {/* Nagłówek twierdzenia */}

      <div className="p-5 sm:p-6 space-y-4">
        <div className="flex justify-between items-center gap-3 flex-wrap">
          <span className="text-xs uppercase tracking-wider text-gray-400 font-semibold">
            Twierdzenie {String(claim.index).padStart(2, '0')}
          </span>

          <Badge color={verdict.color}>
            {verdict.icon} {claim.outcome?.label || verdict.label}
          </Badge>
        </div>

        <h3 className="text-base sm:text-lg font-medium leading-relaxed">
          {claim.text}
        </h3>

        {claim.summary && (
          <p className="text-sm text-gray-600 leading-relaxed">
            {claim.summary}
          </p>
        )}

        {/* Siła podstawy */}

        {claim.strengthLabel && (
          <div className="flex items-center gap-2 flex-wrap">
            <span className="text-xs text-gray-500">Siła podstawy:</span>

            <Badge color={getRiskColor(claim.strength)}>
              {claim.strengthLabel}
            </Badge>
          </div>
        )}

        {/* Podstawowe statystyki */}

        <div className="flex flex-wrap gap-x-5 gap-y-2 text-xs text-gray-500">
          <span>
            <strong className="text-gray-800">{includedEvidence.length}</strong>{' '}
            uwzględnionych dowodów
          </span>

          <span>
            <strong className="text-gray-800">{context.length}</strong>{' '}
            materiałów kontekstowych
          </span>
        </div>

        <button
          onClick={() => setExpanded(!expanded)}
          className="text-sm text-blue-600 hover:text-blue-800 font-medium"
        >
          {expanded ? 'Ukryj szczegóły ↑' : 'Zobacz dowody i szczegóły ↓'}
        </button>
      </div>

      {/* Szczegóły */}

      {expanded && (
        <div className="border-t border-gray-100 bg-gray-50/70 p-5 sm:p-6 space-y-6">
          {/* Dowody */}

          <section>
            <h4 className="font-semibold text-sm mb-3">
              Dowody i źródła ({evidence.length})
            </h4>

            {evidence.length > 0 ? (
              <div className="space-y-3">
                {evidence.map((item, index) => (
                  <EvidenceCard key={item.sourceName + index} evidence={item} />
                ))}
              </div>
            ) : (
              <p className="text-sm text-gray-500 bg-white border rounded-xl p-4">
                Nie znaleziono dowodów, które można uwzględnić w werdykcie.
              </p>
            )}
          </section>

          {/* Kontekst */}

          {context.length > 0 && (
            <section>
              <h4 className="font-semibold text-sm mb-3">
                Materiały kontekstowe
              </h4>

              <div className="space-y-2">
                {context.map((item, index) => (
                  <EvidenceCard key={index} evidence={item} />
                ))}
              </div>
            </section>
          )}

          {/* Nieznane kwestie */}

          {unknowns.length > 0 && (
            <section>
              <h4 className="font-semibold text-sm mb-3">
                Niewyjaśnione kwestie
              </h4>

              <div className="bg-amber-50 border border-amber-200 rounded-xl p-4">
                <ul className="space-y-2">
                  {unknowns.map((item, index) => (
                    <li
                      key={index}
                      className="text-xs text-amber-900 leading-relaxed"
                    >
                      • {item}
                    </li>
                  ))}
                </ul>
              </div>
            </section>
          )}

          {/* Założenia */}

          {claim.premises?.length > 0 && (
            <details className="bg-white border border-gray-200 rounded-xl p-4">
              <summary className="cursor-pointer text-sm font-medium">
                Założenia i podstawa werdyktu
              </summary>

              <ul className="mt-3 space-y-2">
                {claim.premises.map((item, index) => (
                  <li
                    key={index}
                    className="text-xs text-gray-600 leading-relaxed"
                  >
                    • {item}
                  </li>
                ))}
              </ul>
            </details>
          )}

          {/* Porównanie */}

          <Comparison comparison={claim.chain?.comparison} />

          {/* Historia analizy */}

          {claim.chain?.steps?.length > 0 && (
            <section>
              <button
                onClick={() => setShowProcess(!showProcess)}
                className="text-sm font-medium text-blue-600 hover:text-blue-800"
              >
                {showProcess
                  ? 'Ukryj przebieg analizy ↑'
                  : 'Pokaż przebieg analizy ↓'}
              </button>

              {showProcess && (
                <div className="mt-4 space-y-4">
                  {claim.chain.steps.map((step) => (
                    <div key={step.order} className="flex gap-3">
                      <div className="w-7 h-7 rounded-full bg-blue-50 text-blue-700 flex items-center justify-center text-xs font-semibold shrink-0">
                        {step.order}
                      </div>

                      <div className="min-w-0">
                        <h5 className="text-sm font-medium">{step.title}</h5>

                        <ul className="mt-2 space-y-2">
                          {step.details?.map((detail, index) => (
                            <li
                              key={index}
                              className="text-xs text-gray-600 leading-relaxed break-words"
                            >
                              {detail}
                            </li>
                          ))}
                        </ul>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>
          )}
        </div>
      )}
    </article>
  );
}

// ==========================================
// STATUS WYSZUKIWANIA
// ==========================================

export function SourcesStatus({ sources }) {
  const [expanded, setExpanded] = useState(false);

  const statusConfig = {
    Ok: { label: 'Zakończono', color: 'green' },
    Failed: { label: 'Błąd', color: 'red' },
    NoResults: { label: 'Brak wyników', color: 'gray' },
  };

  return (
    <section className="bg-white border border-gray-200 rounded-2xl p-5">
      <button
        onClick={() => setExpanded(!expanded)}
        className="w-full flex items-center justify-between text-left"
      >
        <div>
          <h2 className="font-semibold text-base">Wykorzystane źródła</h2>

          <p className="text-xs text-gray-500 mt-1">
            {sources.length} usług wyszukiwania i weryfikacji
          </p>
        </div>

        <span className="text-blue-600 text-sm">
          {expanded ? 'Ukryj ↑' : 'Pokaż ↓'}
        </span>
      </button>

      {expanded && (
        <div className="mt-5 divide-y divide-gray-100">
          {sources.map((source) => {
            const status =
              statusConfig[source.status] || statusConfig.NoResults;

            return (
              <div
                key={source.sourceId}
                className="py-3 flex items-center justify-between gap-3"
              >
                <div>
                  <p className="text-sm font-medium">{source.name}</p>

                  {source.message && (
                    <p className="text-xs text-gray-500 mt-1">
                      {source.message}
                    </p>
                  )}
                </div>

                <Badge color={status.color}>{status.label}</Badge>
              </div>
            );
          })}
        </div>
      )}
    </section>
  );
}
