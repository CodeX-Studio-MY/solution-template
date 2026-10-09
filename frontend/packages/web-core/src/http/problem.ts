import { errorCodes, type ErrorCode } from "../generated/errors";

/** A field-level validation error, as the API returns it in ProblemDetails.errors. */
export interface FieldError {
  field: string;
  code?: string;
  message: string;
}

/** RFC 9457 ProblemDetails, plus the platform's stable `code` and `errors`. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  message?: string;
  errors?: FieldError[] | Record<string, string[]>;
  traceId?: string;
}

/** Every failed API call becomes one of these, so screens handle one kind of error. */
export class ApiError extends Error {
  readonly status: number;
  readonly code: string | undefined;
  readonly traceId: string | undefined;
  readonly fieldErrors: FieldError[];

  constructor(status: number, problem: ProblemDetails | undefined) {
    super(problem?.message ?? problem?.detail ?? problem?.title ?? `The request failed (${status}).`);
    this.name = "ApiError";
    this.status = status;
    this.code = problem?.code;
    this.traceId = problem?.traceId;
    this.fieldErrors = normaliseFieldErrors(problem?.errors);
  }

  /** True for a known platform or module error code, which lets a screen show a specific message. */
  is(code: ErrorCode): boolean {
    return this.code === code;
  }

  /** The i18n key for this error's message, when the code is in the catalog. */
  get messageKey(): string | undefined {
    return this.code !== undefined && this.code in errorCodes ? errorCodes[this.code as ErrorCode].messageKey : undefined;
  }

  /** The error for one form field, if the server rejected it. */
  forField(field: string): FieldError | undefined {
    return this.fieldErrors.find((e) => e.field.toLowerCase() === field.toLowerCase());
  }
}

function normaliseFieldErrors(errors: ProblemDetails["errors"]): FieldError[] {
  if (!errors) return [];
  if (Array.isArray(errors)) return errors;
  return Object.entries(errors).flatMap(([field, messages]) => messages.map((message) => ({ field, message })));
}

/** Reads the ProblemDetails body of a failed response (tolerating anything else a proxy returns). */
export async function readProblem(response: Response): Promise<ApiError> {
  let problem: ProblemDetails | undefined;
  try {
    problem = (await response.clone().json()) as ProblemDetails;
  } catch {
    problem = undefined;
  }
  return new ApiError(response.status, problem);
}
