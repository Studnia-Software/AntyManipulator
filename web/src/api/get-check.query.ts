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
  });

  return {
    data: query.data,
    isLoading: query.isPending,
    isError: query.isError,
  };
};
