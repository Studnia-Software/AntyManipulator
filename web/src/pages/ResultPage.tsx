import { Link, useNavigate, useSearchParams } from 'react-router';
import { useMemo } from 'react';
import { useGetCheckQuery } from '../api';
import {
  CheckLoader,
  ClaimCard,
  formatDate,
  Overview,
  SourcesStatus,
} from '../components';

export const ResultPage = () => {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const searchQuery = searchParams.get('q');

  // Zapytanie liczone od razu z adresu: wcześniej pierwszy render wysyłał do API puste dane.
  const query = useMemo(() => {
    const value = searchQuery?.trim() || null;
    const isUrl =
      value?.startsWith('http://') || value?.startsWith('https://') || false;
    return { text: isUrl ? null : value, url: isUrl ? value : null };
  }, [searchQuery]);

  const { data: result, isLoading, isError } = useGetCheckQuery(query);

  const onBack = () => {
    navigate('/');
  };

  // Kolejność ma znaczenie: najpierw ładowanie i błąd, dopiero potem brak danych.
  if (isLoading) {
    return <CheckLoader />;
  }

  if (isError) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center gap-4 px-5 text-center">
        <p>Wystąpił błąd techniczny podczas weryfikacji. To nie jest ocena treści.</p>
        <button
          onClick={onBack}
          className="text-blue-600 hover:underline font-semibold"
        >
          Spróbuj ponownie
        </button>
      </div>
    );
  }

  if (!result) {
    return (
      <div className="min-h-screen flex flex-col items-center justify-center gap-4 px-5 text-center">
        <p>Brak danych analizy.</p>
        <Link to="/" className="text-blue-600 hover:underline font-semibold">
          Wróć do wyszukiwania
        </Link>
      </div>
    );
  }

  const claims = result.claims || [];
  const input = result.input || {};

  return (
    <main className=" bg-[#F8F9FA] text-gray-800">
      {/* HEADER */}

      <header className="bg-white border-b border-gray-200 sticky   px-5 h-16 flex">
        <span className="font-bold content-center">
          <span className="text-blue-500">Anty</span>manipulator
        </span>
      </header>

      {/* MAIN */}

      <div className="max-w-5xl mx-auto px-5 py-8 space-y-6">
        <div className="max-w-5xl mx-auto flex items-center justify-between gap-4 mb-8">
          <Link to="/" className="text-blue-600 hover:underline font-semibold">
            &lt;&lt; Nowa analiza
          </Link>
        </div>

        {/* TITLE */}

        <section>
          <p className="text-xs text-gray-400 uppercase tracking-widest mb-2">
            Raport fact-checkingowy
          </p>

          <h1 className="text-2xl sm:text-3xl font-semibold">Wyniki analizy</h1>

          <p className="text-sm text-gray-500 mt-2">
            Zweryfikowaliśmy twierdzenia i porównaliśmy je z dostępnymi
            źródłami.
          </p>
        </section>

        {/* INJECTION WARNING */}

        {result.injectionSuspected && (
          <section className="bg-red-50 border border-red-200 rounded-xl p-4">
            <div className="flex gap-3">
              <span className="text-red-600 text-lg">⚠</span>

              <div>
                <h2 className="text-sm font-semibold text-red-800">
                  Wykryto podejrzenie prompt injection
                </h2>

                <p className="text-xs text-red-700 mt-1">
                  Analizowany materiał może zawierać instrukcje próbujące
                  wpłynąć na działanie systemu.
                </p>
              </div>
            </div>
          </section>
        )}

        {/* INPUT MATERIAL */}

        <section className="bg-white border border-gray-200 rounded-2xl p-5 sm:p-6">
          <p className="text-xs font-semibold tracking-wider text-gray-400 mb-3">
            ANALIZOWANY MATERIAŁ
          </p>

          <p className="text-sm font-medium leading-relaxed">
            {input.pageTitle || 'Analizowany materiał'}
          </p>

          {input.pagePublisher && (
            <p className="text-xs text-gray-500 mt-2">
              Wydawca: {input.pagePublisher}
            </p>
          )}

          {input.url && (
            <a
              href={input.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-xs text-blue-600 hover:underline mt-3 inline-block break-all"
            >
              Otwórz oryginalny materiał ↗
            </a>
          )}

          <div className="mt-4 pt-4 border-t border-gray-100 flex flex-wrap gap-4 text-xs text-gray-400">
            <span>Rozpoczęto: {formatDate(result.startedAt)}</span>

            <span>Zakończono: {formatDate(result.completedAt)}</span>
          </div>
        </section>

        {/* OVERVIEW */}

        <Overview claims={claims} />

        {/* CLAIMS */}

        <section className="space-y-4">
          <div>
            <h2 className="text-xl font-semibold">Zweryfikowane twierdzenia</h2>

            <p className="text-sm text-gray-500 mt-1">
              Każde twierdzenie jest oceniane osobno na podstawie dostępnych
              dowodów.
            </p>
          </div>

          {claims.length > 0 ? (
            claims.map((claim) => <ClaimCard key={claim.index} claim={claim} />)
          ) : (
            <div className="bg-white border border-gray-200 rounded-xl p-6 text-center">
              <p className="text-sm text-gray-500">
                Nie wykryto żadnych twierdzeń do weryfikacji.
              </p>
            </div>
          )}
        </section>

        {/* SEARCH SOURCES */}

        {result.sourcesConsulted?.length > 0 && (
          <SourcesStatus sources={result.sourcesConsulted} />
        )}

        {/* LIMITATIONS */}

        {result.limitations?.length > 0 && (
          <section className="bg-white border border-gray-200 rounded-2xl p-5 sm:p-6">
            <h2 className="font-semibold text-base mb-3">
              Ograniczenia analizy
            </h2>

            <p className="text-xs text-gray-500 mb-4">
              Poniższe informacje opisują zakres i ograniczenia przeprowadzonej
              weryfikacji.
            </p>

            <ul className="space-y-3">
              {result.limitations.map((item, index) => (
                <li
                  key={index}
                  className="flex gap-3 text-xs text-gray-600 leading-relaxed"
                >
                  <span className="text-gray-400 shrink-0">•</span>
                  {item}
                </li>
              ))}
            </ul>
          </section>
        )}

        {/* FOOTER */}

        <footer className="text-center text-xs text-gray-400 py-6">
          Antymanipulator - HackNation 2026
        </footer>
      </div>
    </main>
  );
};
