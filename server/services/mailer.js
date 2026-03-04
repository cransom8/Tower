'use strict';

const nodemailer = require('nodemailer');

const SMTP_USER = process.env.SMTP_USER;
const SMTP_PASS = process.env.SMTP_PASS;
const SMTP_FROM = process.env.SMTP_FROM || SMTP_USER;

let _transporter = null;

function getTransporter() {
  if (!_transporter) {
    if (!SMTP_USER || !SMTP_PASS) {
      throw new Error('SMTP_USER and SMTP_PASS env vars are required for email');
    }
    _transporter = nodemailer.createTransport({
      host:   'smtp.gmail.com',
      port:   587,
      secure: false, // STARTTLS
      auth:   { user: SMTP_USER, pass: SMTP_PASS },
    });
  }
  return _transporter;
}

async function sendPasswordReset(toEmail, resetUrl) {
  const transport = getTransporter();
  await transport.sendMail({
    from:    `"Ransom Forge" <${SMTP_FROM}>`,
    to:      toEmail,
    subject: 'Reset your Ransom Forge password',
    text:
      `You requested a password reset.\n\n` +
      `Click the link below to set a new password. It expires in 1 hour.\n\n` +
      `${resetUrl}\n\n` +
      `If you did not request this, you can safely ignore this email.`,
    html:
      `<div style="font-family:sans-serif;max-width:480px;margin:0 auto">` +
      `<h2 style="color:#e8a828">Ransom Forge</h2>` +
      `<p>You requested a password reset.</p>` +
      `<p style="margin:24px 0">` +
      `<a href="${resetUrl}" style="background:#e8a828;color:#000;padding:10px 24px;border-radius:6px;text-decoration:none;font-weight:bold">Reset Password</a>` +
      `</p>` +
      `<p style="color:#888;font-size:12px">This link expires in 1 hour.<br>If you did not request this, you can safely ignore this email.</p>` +
      `</div>`,
  });
}

async function sendEmailVerification(toEmail, verifyUrl) {
  const transport = getTransporter();
  await transport.sendMail({
    from:    `"Ransom Forge" <${SMTP_FROM}>`,
    to:      toEmail,
    subject: 'Verify your Ransom Forge email',
    text:
      `Welcome to Ransom Forge!\n\n` +
      `Click the link below to verify your email address. It expires in 24 hours.\n\n` +
      `${verifyUrl}\n\n` +
      `If you did not create an account, you can safely ignore this email.`,
    html:
      `<div style="font-family:sans-serif;max-width:480px;margin:0 auto">` +
      `<h2 style="color:#e8a828">Ransom Forge</h2>` +
      `<p>Welcome! Please verify your email address to activate your account.</p>` +
      `<p style="margin:24px 0">` +
      `<a href="${verifyUrl}" style="background:#e8a828;color:#000;padding:10px 24px;border-radius:6px;text-decoration:none;font-weight:bold">Verify Email</a>` +
      `</p>` +
      `<p style="color:#888;font-size:12px">This link expires in 24 hours.<br>If you did not create an account, you can safely ignore this email.</p>` +
      `</div>`,
  });
}

module.exports = { sendPasswordReset, sendEmailVerification };
