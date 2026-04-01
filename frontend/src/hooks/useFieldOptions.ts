import { useQuery } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';

import { api } from '@/lib/api';
import type { FieldOption, FieldOptionsResponse } from '@/types/api';

function useFieldOptionsQuery() {
  return useQuery<FieldOptionsResponse>({
    queryKey: ['capabilities', 'field-options'],
    queryFn: () =>
      api
        .get<FieldOptionsResponse>('/capabilities/field-options')
        .then((response) => response.data),
    staleTime: Infinity,
  });
}

/**
 * Returns field options for a specific capability and field.
 * Returns the options array (for dropdowns) and a display label resolver.
 */
function useFieldOptions(capabilitySlug: string, fieldName: string) {
  const { data, isLoading } = useFieldOptionsQuery();

  const options: FieldOption[] = useMemo(() => {
    if (!data) return [];
    const group = data.fieldOptions.find(
      (fo) => fo.capabilitySlug === capabilitySlug && fo.fieldName === fieldName,
    );
    return group?.options ?? [];
  }, [data, capabilitySlug, fieldName]);

  const getDisplayLabel = useCallback(
    (value: string): string => {
      const match = options.find((option) => option.value === value);
      return match?.displayName ?? value;
    },
    [options],
  );

  return { options, getDisplayLabel, isLoading };
}

export { useFieldOptions, useFieldOptionsQuery };
