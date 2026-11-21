import { useState, useRef, useEffect } from "react";

export default function LandingSearch() {
  const [query, setQuery] = useState("");
  const [image, setImage] = useState(null);
  const [dragging, setDragging] = useState(false);

  const fileInputRef = useRef(null);
  const textareaRef = useRef(null);

  // Obsługa ładowania obrazu
  const handleImage = (file) => {
    if (!file || !file.type.startsWith("image/")) return;

    if (image?.url) {
      URL.revokeObjectURL(image.url);
    }

    setImage({
      file,
      url: URL.createObjectURL(file),
    });
  };

  // Obsługa wklejania tekstu i screenshotów
  const handlePaste = (e) => {
    const items = e.clipboardData?.items;

    if (!items) return;

    for (const item of items) {
      if (item.type.startsWith("image/")) {
        const file = item.getAsFile();

        if (file) {
          handleImage(file);
          e.preventDefault();
          return;
        }
      }
    }
  };

  // Usuwanie obrazu
  const removeImage = () => {
    if (image?.url) {
      URL.revokeObjectURL(image.url);
    }

    setImage(null);

    if (fileInputRef.current) {
      fileInputRef.current.value = "";
    }
  };

  // Drag & Drop
  const handleDrop = (e) => {
    e.preventDefault();
    setDragging(false);

    const file = e.dataTransfer.files?.[0];

    if (file) {
      handleImage(file);
    }
  };

  // Wyszukiwanie
  const handleSearch = () => {
    if (!query.trim() && !image) return;

    // Tutaj podłączysz swoje API
    console.log("Wyszukiwanie:", {
      text: query,
      image: image?.file || null,
    });
  };

  // Enter uruchamia wyszukiwanie
  const handleKeyDown = (e) => {
    if (e.key === "Enter" && !e.shiftKey) {
      e.preventDefault();
      handleSearch();
    }
  };

  // Sprzątanie URL obrazu
  useEffect(() => {
    return () => {
      if (image?.url) {
        URL.revokeObjectURL(image.url);
      }
    };
  }, [image]);

  return (
    <main className="min-h-screen bg-white flex flex-col items-center px-5">

      {/* Główna zawartość */}
      <div className="w-full max-w-2xl flex flex-col items-center mt-[16vh] sm:mt-[20vh]">

        {/* Logo */}
        <h1 className="text-4xl sm:text-6xl font-bold tracking-tight mb-4 select-none font-montserrat">
          {"!Antymanipulator".split("").map((letter, index) => {
            const colors = [
              "#4285F4",
              "#EA4335",
              "#FBBC05",
              "#4285F4",
              "#34A853",
              "#EA4335",
            ];

            return (
              <span
                key={index}
                style={{
                  color: colors[index % colors.length],
                }}
              >
                {letter}
              </span>
            );
          })}
        </h1>

        <p className="text-sm text-gray-500 mb-9 text-center">
          Sprawdź, zanim uwierzysz.
        </p>

        {/* Pole wyszukiwania */}
        <div
          onDragOver={(e) => {
            e.preventDefault();
            setDragging(true);
          }}
          onDragLeave={() => setDragging(false)}
          onDrop={handleDrop}
          className={`
            w-full rounded-3xl border px-5 py-4
            transition-all duration-200
            shadow-sm hover:shadow-md
            ${dragging
              ? "border-blue-500 bg-blue-50"
              : "border-gray-300 bg-white"}
          `}
        >

          <textarea
            ref={textareaRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onPaste={handlePaste}
            onKeyDown={handleKeyDown}
            placeholder="Wpisz tekst lub wklej screenshot..."
            rows={3}
            className="
              w-full resize-none outline-none
              text-base text-gray-800
              placeholder:text-gray-400
              bg-transparent
            "
          />

          {/* Podgląd obrazu */}
          {image && (
            <div className="relative mt-3 inline-block">
              <img
                src={image.url}
                alt="Podgląd załączonego obrazu"
                className="
                  max-h-44 max-w-full rounded-xl
                  object-contain border border-gray-200
                "
              />

              <button
                onClick={removeImage}
                aria-label="Usuń obraz"
                className="
                  absolute -top-2 -right-2
                  w-7 h-7 rounded-full
                  bg-gray-800 text-white
                  hover:bg-red-500 transition
                  flex items-center justify-center
                "
              >
                ×
              </button>
            </div>
          )}

          {/* Dolny pasek pola wyszukiwania */}
          <div className="flex items-center justify-between mt-4">

            <div className="flex items-center gap-3">

              <button
                onClick={() => fileInputRef.current?.click()}
                className="
                  p-2 rounded-full text-gray-500
                  hover:bg-gray-100 hover:text-blue-500
                  transition
                "
                title="Dodaj obraz"
                aria-label="Dodaj obraz"
              >
                <svg
                  width="22"
                  height="22"
                  viewBox="0 0 24 24"
                  fill="none"
                  stroke="currentColor"
                  strokeWidth="1.7"
                >
                  <rect x="3" y="3" width="18" height="18" rx="3" />
                  <circle cx="8.5" cy="8.5" r="1.5" />
                  <path d="m21 15-5-5L5 21" />
                </svg>
              </button>

              <span className="text-xs text-gray-400 hidden sm:block">
                Wklej obraz lub przeciągnij plik
              </span>

            </div>

            <input
              ref={fileInputRef}
              type="file"
              accept="image/*"
              className="hidden"
              onChange={(e) => {
                handleImage(e.target.files?.[0]);
              }}
            />

            <button
              onClick={handleSearch}
              disabled={!query.trim() && !image}
              className="
                px-5 py-2.5 rounded-full
                bg-blue-500 text-white
                text-sm font-medium
                hover:bg-blue-600
                disabled:bg-gray-300
                disabled:cursor-not-allowed
                transition
              "
            >
              Wyszukaj
            </button>

          </div>
        </div>

        {/* Dodatkowa informacja */}
        <p className="mt-7 text-xs text-gray-400 text-center">
          Możesz wkleić tekst, screenshot lub wybrać obraz z urządzenia.
        </p>

      </div>

      {/* Stopka */}
      <footer className="mt-auto py-7 text-xs text-gray-400">
        !Antymanipulator
      </footer>

    </main>
  );
}
