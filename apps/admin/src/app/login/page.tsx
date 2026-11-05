"use client";

import { useMutation } from "@tanstack/react-query";
import { zodResolver } from "@hookform/resolvers/zod";
import { ArrowRight, Bike, LockKeyhole, ShieldCheck } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { z } from "zod";
import { useAuth } from "@/lib/auth";
import { defaultTenantId } from "@/lib/api";
import { ApiError } from "@/lib/api";

const schema = z.object({
  tenantId: z.string().uuid("Enter a valid workspace ID."),
  identifier: z.string().min(1, "Enter your email address or Ugandan phone number."),
  password: z.string().min(1, "Enter your password."),
});
type LoginValues = z.infer<typeof schema>;

export default function LoginPage() {
  const { session, ready, signIn } = useAuth();
  const router = useRouter();
  const form = useForm<LoginValues>({
    resolver: zodResolver(schema),
    defaultValues: { tenantId: defaultTenantId, identifier: "", password: "" },
  });
  const mutation = useMutation({
    mutationFn: signIn,
    onSuccess: () => router.replace("/"),
  });

  useEffect(() => {
    if (ready && session) router.replace("/");
  }, [ready, session, router]);

  const errorMessage = mutation.error instanceof ApiError
    ? mutation.error.status === 401
      ? "Email/phone or password is incorrect."
      : mutation.error.message
    : mutation.error?.message;

  return (
    <main className="login-page">
      <section className="login-visual">
        <div className="login-brand"><span className="brand-mark"><Bike size={21} /></span><strong>BodaBoss</strong><span>ADMIN PORTAL</span></div>
        <div className="visual-copy">
          <span className="visual-kicker"><span className="online-dot" /> OPERATIONS, IN MOTION</span>
          <h1>Your fleet.<br /><span>Your command.</span></h1>
          <p>One clear view of your operations, people, and payments—connected to the data your business already has.</p>
        </div>
        <div className="visual-footer"><ShieldCheck size={17} /> Secure tenant-based access <span>·</span> Built for fleet operations</div>
        <div className="visual-orbit orbit-one" /><div className="visual-orbit orbit-two" /><div className="visual-pattern" />
      </section>
      <section className="login-panel">
        <div className="login-form-wrap">
          <div className="login-mobile-brand"><span className="brand-mark"><Bike size={20} /></span><strong>BodaBoss</strong></div>
          <div className="login-heading"><span className="eyebrow">WELCOME BACK</span><h2>Sign in to your account</h2><p>Use your BodaBoss workspace credentials to continue.</p></div>
          <form onSubmit={form.handleSubmit((values) => mutation.mutate(values))} className="login-form">
            <label htmlFor="tenantId">Workspace ID</label>
            <input id="tenantId" autoComplete="organization" {...form.register("tenantId")} aria-invalid={Boolean(form.formState.errors.tenantId)} />
            {form.formState.errors.tenantId && <span className="field-error">{form.formState.errors.tenantId.message}</span>}
            <label htmlFor="identifier">Email or phone number</label>
            <div className="input-wrap"><input id="identifier" autoComplete="username" placeholder="you@company.com or +256…" {...form.register("identifier")} aria-invalid={Boolean(form.formState.errors.identifier)} /></div>
            {form.formState.errors.identifier && <span className="field-error">{form.formState.errors.identifier.message}</span>}
            <label htmlFor="password">Password</label>
            <div className="input-wrap"><LockKeyhole size={17} /><input id="password" type="password" autoComplete="current-password" placeholder="Enter your password" {...form.register("password")} aria-invalid={Boolean(form.formState.errors.password)} /></div>
            {form.formState.errors.password && <span className="field-error">{form.formState.errors.password.message}</span>}
            {errorMessage && <div role="alert" className="form-alert">{errorMessage}</div>}
            <button className="button button-primary login-submit" type="submit" disabled={mutation.isPending}>
              {mutation.isPending ? <><span className="spinner spinner-light" /> Signing in…</> : <>Sign in <ArrowRight size={17} /></>}
            </button>
          </form>
          <div className="login-security"><LockKeyhole size={14} /> Your session is secured with a tenant-scoped access token.</div>
          <p className="login-caption">Access is provisioned by your BodaBoss workspace administrator.</p>
        </div>
      </section>
    </main>
  );
}
