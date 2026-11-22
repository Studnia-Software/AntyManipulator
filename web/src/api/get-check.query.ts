import { useQuery } from '@tanstack/react-query';

export type CheckType = {
  text: string | null;
  url: string | null;
};

const baseUrl = import.meta.env.VITE_API_URL;

export const useGetCheckQuery = (type: CheckType) => {
  const query = useQuery({
    queryKey: ['repoData', type],
    queryFn: async () => {
      const response = await fetch(`${baseUrl}/api/checks`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(type),
      });
      if (!response.ok) {
        throw new Error('Network response was not ok');
      }
      return response.json();
    },
    // Bez treści nie ma czego sprawdzać.
    enabled: Boolean(type.text || type.url),
    // Sprawdzenie trwa kilkadziesiąt sekund i kosztuje zapytania do modelu i źródeł:
    // nie ponawiamy go automatycznie ani przy powrocie do karty.
    retry: false,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });

  return {
    data: query.data,
    // isLoading = trwa pierwsze pobieranie; isPending jest true także dla wyłączonego zapytania.
    isLoading: query.isLoading,
    isError: query.isError,
  };
};
