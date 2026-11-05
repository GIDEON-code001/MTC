import 'dart:async';

import 'package:dio/dio.dart';
import 'package:firebase_core/firebase_core.dart';
import 'package:firebase_messaging/firebase_messaging.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:geolocator/geolocator.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/intl.dart';

const apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: 'http://localhost:8080',
);
const defaultTenantId = String.fromEnvironment(
  'TENANT_ID',
  defaultValue: '11111111-1111-4111-8111-111111111111',
);

final _container = ProviderContainer();
final _authController = _container.read(authProvider.notifier);
final _router = GoRouter(
  initialLocation: '/home',
  refreshListenable: _authController.refreshListenable,
  redirect: (context, state) {
    final path = state.matchedLocation;
    if (_authController.state.isLoading && path != '/login') return '/loading';
    if (_authController.state.isLoading) return null;
    final loggedIn = _authController.state.valueOrNull != null;
    if (!loggedIn && path != '/login') return '/login';
    if (loggedIn && (path == '/login' || path == '/loading')) return '/home';
    return null;
  },
  routes: [
    GoRoute(
      path: '/loading',
      builder: (context, state) => const Scaffold(
        body: Center(child: CircularProgressIndicator()),
      ),
    ),
    GoRoute(path: '/login', builder: (context, state) => const LoginScreen()),
    StatefulShellRoute.indexedStack(
      builder: (context, state, navigationShell) =>
          RiderShell(navigationShell: navigationShell),
      branches: [
        StatefulShellBranch(
          routes: [GoRoute(path: '/home', builder: (context, state) => const HomeScreen())],
        ),
        StatefulShellBranch(
          routes: [GoRoute(path: '/jobs', builder: (context, state) => const JobsScreen())],
        ),
        StatefulShellBranch(
          routes: [GoRoute(path: '/report', builder: (context, state) => const ReportScreen())],
        ),
        StatefulShellBranch(
          routes: [GoRoute(path: '/payments', builder: (context, state) => const PaymentsScreen())],
        ),
        StatefulShellBranch(
          routes: [
            GoRoute(path: '/notifications', builder: (context, state) => const NotificationsScreen()),
          ],
        ),
      ],
    ),
  ],
);

void main() {
  runApp(
    UncontrolledProviderScope(
      container: _container,
      child: const BodaBossApp(),
    ),
  );
}

class BodaBossApp extends StatelessWidget {
  const BodaBossApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp.router(
        title: 'BodaBoss Rider',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          useMaterial3: true,
          colorScheme: ColorScheme.fromSeed(
            seedColor: const Color(0xFF176B58),
            surface: const Color(0xFFF7F8F6),
          ),
          scaffoldBackgroundColor: const Color(0xFFF7F8F6),
          cardTheme: CardThemeData(
            color: Colors.white,
            elevation: 0,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(16),
              side: const BorderSide(color: Color(0xFFE9EDE9)),
            ),
          ),
        ),
        routerConfig: _router,
      );
}

class RiderSession {
  const RiderSession({
    required this.accessToken,
    required this.refreshToken,
    required this.expiresAt,
    required this.role,
  });

  final String accessToken;
  final String refreshToken;
  final String expiresAt;
  final String role;

  Map<String, String> toJson() => {
        'accessToken': accessToken,
        'refreshToken': refreshToken,
        'expiresAt': expiresAt,
        'role': role,
      };

  factory RiderSession.fromJson(Map<String, dynamic> json) => RiderSession(
        accessToken: json['accessToken'] as String,
        refreshToken: json['refreshToken'] as String,
        expiresAt: json['expiresAt'] as String,
        role: json['role'] as String,
      );
}

class ApiService {
  ApiService()
      : _storage = const FlutterSecureStorage(),
        _dio = Dio(
          BaseOptions(
            baseUrl: apiBaseUrl,
            connectTimeout: const Duration(seconds: 15),
            receiveTimeout: const Duration(seconds: 20),
            headers: {'Accept': 'application/json'},
          ),
        ) {
    _dio.interceptors.add(
      InterceptorsWrapper(
        onRequest: (options, handler) async {
          final token = await _storage.read(key: 'accessToken');
          if (token != null) options.headers['Authorization'] = 'Bearer $token';
          handler.next(options);
        },
        onError: (error, handler) async {
          final options = error.requestOptions;
          if (error.response?.statusCode != 401
              || options.extra['refreshRetried'] == true
              || options.path.endsWith('/auth/login')
              || options.path.endsWith('/auth/refresh')) {
            handler.next(error);
            return;
          }

          try {
            final refreshed = await (_refreshing ??= _refreshSession()
                .whenComplete(() => _refreshing = null));
            options.extra['refreshRetried'] = true;
            options.headers['Authorization'] = 'Bearer ${refreshed.accessToken}';
            handler.resolve(await _dio.fetch<dynamic>(options));
          } on DioException catch (refreshError) {
            if (refreshError.response?.statusCode == 401) {
              await logout();
              onSessionExpired?.call();
            }
            handler.next(error);
          }
        },
      ),
    );
  }

  final FlutterSecureStorage _storage;
  final Dio _dio;
  Future<RiderSession>? _refreshing;
  VoidCallback? onSessionExpired;

  Future<RiderSession> _refreshSession() async {
    final refreshToken = await _storage.read(key: 'refreshToken');
    if (refreshToken == null) {
      await logout();
      onSessionExpired?.call();
      throw DioException(
        requestOptions: RequestOptions(path: '/api/v1/auth/refresh'),
        message: 'A refresh token is unavailable.',
      );
    }

    final response = await Dio().post<Map<String, dynamic>>(
      '$apiBaseUrl/api/v1/auth/refresh',
      data: {'tenantId': defaultTenantId, 'refreshToken': refreshToken},
      options: Options(
        headers: {'Content-Type': 'application/json', 'Accept': 'application/json'},
        receiveTimeout: const Duration(seconds: 20),
      ),
    );
    final session = RiderSession.fromJson(response.data!);
    if (session.role != 'Rider') {
      throw StateError('This account does not have rider access.');
    }
    await _storage.write(key: 'accessToken', value: session.accessToken);
    await _storage.write(key: 'refreshToken', value: session.refreshToken);
    await _storage.write(key: 'expiresAt', value: session.expiresAt);
    await _storage.write(key: 'role', value: session.role);
    return session;
  }

  Future<RiderSession> login(String identifier, String password) async {
    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/auth/login',
      data: {
        'tenantId': defaultTenantId,
        'identifier': identifier.trim(),
        'password': password,
      },
    );
    final session = RiderSession.fromJson(response.data!);
    await _storage.write(key: 'accessToken', value: session.accessToken);
    await _storage.write(key: 'refreshToken', value: session.refreshToken);
    await _storage.write(key: 'expiresAt', value: session.expiresAt);
    return session;
  }

  Future<RiderSession?> restore() async {
    final token = await _storage.read(key: 'accessToken');
    final refresh = await _storage.read(key: 'refreshToken');
    final expires = await _storage.read(key: 'expiresAt');
    final role = await _storage.read(key: 'role');
    if (token == null || refresh == null || expires == null || role != 'Rider') {
      await logout();
      return null;
    }
    if (DateTime.tryParse(expires)?.isAfter(DateTime.now()) != true) {
      await logout();
      return null;
    }
    return RiderSession(accessToken: token, refreshToken: refresh, expiresAt: expires, role: 'Rider');
  }

  Future<void> logout() async {
    await _storage.deleteAll();
  }

  Future<Map<String, dynamic>> home() async =>
      (await _dio.get<Map<String, dynamic>>('/api/v1/rider/home')).data!;

  Future<List<Map<String, dynamic>>> jobs() async =>
      _list((await _dio.get<List<dynamic>>('/api/v1/rider/jobs')).data);

  Future<List<Map<String, dynamic>>> payments() async =>
      _list((await _dio.get<List<dynamic>>('/api/v1/rider/payments')).data);

  Future<List<Map<String, dynamic>>> notifications() async =>
      _list((await _dio.get<List<dynamic>>('/api/v1/rider/notifications')).data);

  Future<void> updateJob(String id, String action) async {
    await _dio.post<void>('/api/v1/rider/jobs/$id/$action');
  }

  Future<void> submitReport({
    required String issueType,
    required String description,
    Position? position,
  }) async {
    await _dio.post<void>(
      '/api/v1/rider/reports',
      data: {
        'issueType': issueType,
        'description': description,
        'latitude': position?.latitude,
        'longitude': position?.longitude,
      },
    );
  }

  Future<void> updateNotification(String id, String action) async {
    await _dio.put<void>('/api/v1/rider/notifications/$id/$action');
  }

  Future<void> registerPushDevice() async {
    await Firebase.initializeApp();
    final settings = await FirebaseMessaging.instance.requestPermission();
    if (settings.authorizationStatus == AuthorizationStatus.denied) {
      throw StateError('Notification permission was denied.');
    }

    final token = await FirebaseMessaging.instance.getToken();
    if (token == null || token.isEmpty) {
      throw StateError('Firebase did not return a device token.');
    }
    final platform = kIsWeb
        ? 'web'
        : switch (defaultTargetPlatform) {
            TargetPlatform.android => 'android',
            TargetPlatform.iOS => 'ios',
            _ => throw UnsupportedError('Push notifications require Android, iOS, or web.'),
          };
    await _dio.post<void>(
      '/api/v1/rider/devices',
      data: {'fcmToken': token, 'platform': platform},
    );
  }

  static List<Map<String, dynamic>> _list(List<dynamic>? values) =>
      (values ?? []).whereType<Map<String, dynamic>>().toList();
}

final apiProvider = Provider<ApiService>((ref) => ApiService());

final authProvider =
    StateNotifierProvider<AuthController, AsyncValue<RiderSession?>>(
  (ref) => AuthController(ref.read(apiProvider)),
);

class AuthController extends StateNotifier<AsyncValue<RiderSession?>> {
  AuthController(this._api) : super(const AsyncLoading()) {
    _api.onSessionExpired = () {
      state = const AsyncData(null);
      refreshListenable.notifyListeners();
    };
    unawaited(_restore());
  }

  final ApiService _api;
  final ChangeNotifier refreshListenable = ChangeNotifier();
  String? notificationWarning;

  Future<void> _restore() async {
    try {
      final session = await _api.restore();
      if (session != null) {
        await _registerDevice();
      }
      state = AsyncData(session);
    } catch (error, stackTrace) {
      state = AsyncError(error, stackTrace);
    } finally {
      refreshListenable.notifyListeners();
    }

    Future<void> _registerDevice() async {
      try {
        await _api.registerPushDevice();
        notificationWarning = null;
      } catch (error) {
        notificationWarning = 'Push notifications are unavailable: $error';
      }
    }
  }

  Future<void> signIn(String identifier, String password) async {
    state = const AsyncLoading();
    refreshListenable.notifyListeners();
    try {
      final session = await _api.login(identifier, password);
      await _registerDevice();
      state = AsyncData(session);
    } catch (error, stackTrace) {
      state = AsyncError(error, stackTrace);
      rethrow;
    } finally {
      refreshListenable.notifyListeners();
    }
  }

  Future<void> signOut() async {
    await _api.logout();
    state = const AsyncData(null);
    refreshListenable.notifyListeners();
  }
}

final homeProvider = FutureProvider<Map<String, dynamic>>(
  (ref) => ref.watch(apiProvider).home(),
);
final jobsProvider = FutureProvider<List<Map<String, dynamic>>>(
  (ref) => ref.watch(apiProvider).jobs(),
);
final paymentsProvider = FutureProvider<List<Map<String, dynamic>>>(
  (ref) => ref.watch(apiProvider).payments(),
);
final notificationsProvider = FutureProvider<List<Map<String, dynamic>>>(
  (ref) => ref.watch(apiProvider).notifications(),
);

class RiderShell extends StatelessWidget {
  const RiderShell({required this.navigationShell, super.key});

  final StatefulNavigationShell navigationShell;

  static const _tabs = [
    NavigationDestination(icon: Icon(Icons.home_outlined), selectedIcon: Icon(Icons.home), label: 'Home'),
    NavigationDestination(icon: Icon(Icons.route_outlined), selectedIcon: Icon(Icons.route), label: 'Jobs'),
    NavigationDestination(icon: Icon(Icons.report_gmailerrorred_outlined), selectedIcon: Icon(Icons.report), label: 'Report'),
    NavigationDestination(icon: Icon(Icons.payments_outlined), selectedIcon: Icon(Icons.payments), label: 'Payments'),
    NavigationDestination(icon: Icon(Icons.notifications_none), selectedIcon: Icon(Icons.notifications), label: 'Notifications'),
  ];

  @override
  Widget build(BuildContext context) => Scaffold(
        body: navigationShell,
        bottomNavigationBar: NavigationBar(
          selectedIndex: navigationShell.currentIndex,
          destinations: _tabs,
          onDestinationSelected: (index) => navigationShell.goBranch(
            index,
            initialLocation: index == navigationShell.currentIndex,
          ),
        ),
      );
}

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _identifier = TextEditingController();
  final _password = TextEditingController();
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    _identifier.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _login() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await ref.read(authProvider.notifier).signIn(_identifier.text, _password.text);
    } on DioException catch (error) {
      setState(() => _error = error.response?.statusCode == 401
          ? 'Phone number or password is incorrect.'
          : error.message ?? 'Unable to connect to BodaBoss.');
    } catch (error) {
      setState(() => _error = error.toString());
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
        body: SafeArea(
          child: Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Padding(
                padding: const EdgeInsets.all(24),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const Icon(Icons.two_wheeler, size: 52, color: Color(0xFF176B58)),
                    const SizedBox(height: 12),
                    Text('BodaBoss Rider', style: Theme.of(context).textTheme.headlineMedium, textAlign: TextAlign.center),
                    const SizedBox(height: 8),
                    const Text('Sign in with the phone number provided by your fleet manager.', textAlign: TextAlign.center),
                    const SizedBox(height: 24),
                    TextField(controller: _identifier, keyboardType: TextInputType.phone, decoration: const InputDecoration(labelText: 'Phone number or email')),
                    const SizedBox(height: 12),
                    TextField(controller: _password, obscureText: true, onSubmitted: (_) => _login(), decoration: const InputDecoration(labelText: 'Password')),
                    if (_error != null) ...[
                      const SizedBox(height: 12),
                      Text(_error!, style: TextStyle(color: Theme.of(context).colorScheme.error)),
                    ],
                    const SizedBox(height: 20),
                    FilledButton(
                      onPressed: _busy ? null : _login,
                      child: _busy ? const CircularProgressIndicator() : const Text('Sign in'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      );
}

class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => _Page(
        title: 'Good day',
        onRefresh: () async {
          await ref.refresh(homeProvider.future);
        },
        child: ref.watch(homeProvider).when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (error, _) => _LoadError(message: error.toString(), onRetry: () => ref.invalidate(homeProvider)),
              data: (data) {
                final rider = data['rider'] as Map<String, dynamic>? ?? {};
                final bike = data['motorcycle'] as Map<String, dynamic>?;
                final remittance = data['remittance'] as Map<String, dynamic>? ?? {};
                return ListView(
                  children: [
                    if (_authController.notificationWarning case final warning?)
                      Card(
                        color: Theme.of(context).colorScheme.errorContainer,
                        child: Padding(
                          padding: const EdgeInsets.all(12),
                          child: Text(warning),
                        ),
                      ),
                    Text(rider['fullName'] as String? ?? 'Rider', style: Theme.of(context).textTheme.headlineSmall),
                    const SizedBox(height: 16),
                    _InfoCard(
                      title: 'Today’s remittance',
                      value: _money(remittance['outstandingUgx']),
                      subtitle: '${_money(remittance['amountPaidUgx'])} paid today',
                      icon: Icons.account_balance_wallet_outlined,
                    ),
                    const SizedBox(height: 12),
                    _InfoCard(
                      title: 'Assigned motorcycle',
                      value: bike?['registrationNumber'] as String? ?? 'Not assigned',
                      subtitle: bike == null ? 'Contact your fleet manager' : '${bike['make'] ?? ''} ${bike['model'] ?? ''}',
                      icon: Icons.two_wheeler,
                    ),
                    const SizedBox(height: 12),
                    _InfoCard(
                      title: 'Jobs in progress',
                      value: '${data['activeJobs'] ?? 0}',
                      subtitle: '${data['assignedJobs'] ?? 0} waiting for your response',
                      icon: Icons.route,
                    ),
                  ],
                );
              },
            ),
      );
}

class JobsScreen extends ConsumerWidget {
  const JobsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => _Page(
        title: 'My jobs',
        onRefresh: () async {
          await ref.refresh(jobsProvider.future);
        },
        child: ref.watch(jobsProvider).when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (error, _) => _LoadError(message: error.toString(), onRetry: () => ref.invalidate(jobsProvider)),
              data: (jobs) => jobs.isEmpty
                  ? const _Empty(icon: Icons.route, text: 'No jobs assigned yet.')
                  : ListView.builder(
                      itemCount: jobs.length,
                      itemBuilder: (context, index) {
                        final job = jobs[index];
                        final status = job['status'] as String? ?? '';
                        return Card(
                          child: Padding(
                            padding: const EdgeInsets.all(16),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text('${job['pickupLocation']} → ${job['dropoffLocation']}', style: Theme.of(context).textTheme.titleMedium),
                                if (job['instructions'] != null) Text(job['instructions'] as String),
                                const SizedBox(height: 8),
                                Text('${_money(job['amountUgx'])} · Due ${_date(job['deadline'])} · $status'),
                                const SizedBox(height: 12),
                                Wrap(
                                  spacing: 8,
                                  children: [
                                    if (status == 'Assigned') _JobAction(id: job['id'] as String, action: 'accept', label: 'Accept'),
                                    if (status == 'Accepted') _JobAction(id: job['id'] as String, action: 'start', label: 'Start'),
                                    if (status == 'InProgress') _JobAction(id: job['id'] as String, action: 'complete', label: 'Complete'),
                                    if (status == 'Assigned' || status == 'Accepted' || status == 'InProgress')
                                      _JobAction(id: job['id'] as String, action: 'unable', label: 'Unable to complete', outlined: true),
                                  ],
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
            ),
      );
}

class _JobAction extends ConsumerStatefulWidget {
  const _JobAction({required this.id, required this.action, required this.label, this.outlined = false});

  final String id;
  final String action;
  final String label;
  final bool outlined;

  @override
  ConsumerState<_JobAction> createState() => _JobActionState();
}

class _JobActionState extends ConsumerState<_JobAction> {
  bool _busy = false;

  Future<void> _submit() async {
    setState(() => _busy = true);
    try {
      await ref.read(apiProvider).updateJob(widget.id, widget.action);
      ref.invalidate(jobsProvider);
    } on DioException catch (error) {
      if (mounted) _message(context, error.response?.data?['message']?.toString() ?? 'Could not update this job.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => widget.outlined
      ? OutlinedButton(onPressed: _busy ? null : _submit, child: Text(widget.label))
      : FilledButton(onPressed: _busy ? null : _submit, child: _busy ? const SizedBox.square(dimension: 16, child: CircularProgressIndicator()) : Text(widget.label));
}

class ReportScreen extends ConsumerStatefulWidget {
  const ReportScreen({super.key});

  @override
  ConsumerState<ReportScreen> createState() => _ReportScreenState();
}

class _ReportScreenState extends ConsumerState<ReportScreen> {
  final _description = TextEditingController();
  String _issueType = 'Mechanical';
  Position? _position;
  bool _locating = false;
  bool _sending = false;

  @override
  void dispose() {
    _description.dispose();
    super.dispose();
  }

  Future<void> _captureLocation() async {
    setState(() => _locating = true);
    try {
      if (!await Geolocator.isLocationServiceEnabled()) {
        throw StateError('Turn on location services to attach your GPS position.');
      }
      var permission = await Geolocator.checkPermission();
      if (permission == LocationPermission.denied) permission = await Geolocator.requestPermission();
      if (permission == LocationPermission.denied || permission == LocationPermission.deniedForever) {
        throw StateError('Location permission was not granted.');
      }
      final position = await Geolocator.getCurrentPosition();
      setState(() => _position = position);
    } catch (error) {
      if (mounted) _message(context, error.toString());
    } finally {
      if (mounted) setState(() => _locating = false);
    }
  }

  Future<void> _submit() async {
    if (_description.text.trim().isEmpty) {
      _message(context, 'Describe the issue before submitting.');
      return;
    }
    setState(() => _sending = true);
    try {
      await ref.read(apiProvider).submitReport(
            issueType: _issueType,
            description: _description.text.trim(),
            position: _position,
          );
      _description.clear();
      if (mounted) {
        setState(() => _position = null);
        _message(context, 'Your report was sent to the fleet team.');
      }
    } on DioException catch (error) {
      if (mounted) _message(context, error.response?.data?['message']?.toString() ?? 'Could not send your report.');
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) => _Page(
        title: 'Report an issue',
        child: ListView(
          children: [
            DropdownButtonFormField<String>(
              value: _issueType,
              decoration: const InputDecoration(labelText: 'Issue type'),
              items: const ['Accident', 'Mechanical', 'Tyre', 'Engine', 'Police', 'Theft', 'Fuel', 'Electrical', 'Other']
                  .map((type) => DropdownMenuItem(value: type, child: Text(type)))
                  .toList(),
              onChanged: (value) => setState(() => _issueType = value ?? _issueType),
            ),
            const SizedBox(height: 12),
            TextField(
              controller: _description,
              minLines: 5,
              maxLines: 8,
              maxLength: 4000,
              decoration: const InputDecoration(labelText: 'Describe what happened', alignLabelWithHint: true),
            ),
            OutlinedButton.icon(
              onPressed: _locating ? null : _captureLocation,
              icon: const Icon(Icons.my_location),
              label: Text(_locating ? 'Getting location…' : _position == null ? 'Attach current GPS location' : 'GPS location attached'),
            ),
            const SizedBox(height: 12),
            FilledButton(
              onPressed: _sending ? null : _submit,
              child: _sending ? const CircularProgressIndicator() : const Text('Send report'),
            ),
          ],
        ),
      );
}

class PaymentsScreen extends ConsumerWidget {
  const PaymentsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => _Page(
        title: 'Payments',
        onRefresh: () async {
          await ref.refresh(paymentsProvider.future);
        },
        child: ref.watch(paymentsProvider).when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (error, _) => _LoadError(message: error.toString(), onRetry: () => ref.invalidate(paymentsProvider)),
              data: (payments) => payments.isEmpty
                  ? const _Empty(icon: Icons.payments_outlined, text: 'No payment records yet.')
                  : ListView(
                      children: payments.map((payment) => Card(
                        child: ListTile(
                          leading: const Icon(Icons.payments_outlined),
                          title: Text(_money(payment['amountUgx'])),
                          subtitle: Text('${payment['method']} · ${_date(payment['receivedAt'])}'),
                          trailing: Text(payment['status'] as String? ?? ''),
                        ),
                      )).toList(),
                    ),
            ),
      );
}

class NotificationsScreen extends ConsumerWidget {
  const NotificationsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) => _Page(
        title: 'Notifications',
        onRefresh: () async {
          await ref.refresh(notificationsProvider.future);
        },
        child: ref.watch(notificationsProvider).when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (error, _) => _LoadError(message: error.toString(), onRetry: () => ref.invalidate(notificationsProvider)),
              data: (items) => items.isEmpty
                  ? const _Empty(icon: Icons.notifications_none, text: 'You’re all caught up.')
                  : ListView(
                      children: items.map((item) => Card(
                        child: ListTile(
                          title: Text(item['title'] as String? ?? ''),
                          subtitle: Text('${item['body'] ?? ''}\n${_date(item['createdAt'])}'),
                          isThreeLine: true,
                          trailing: PopupMenuButton<String>(
                            onSelected: (action) async {
                              try {
                                await ref.read(apiProvider).updateNotification(item['id'] as String, action);
                                ref.invalidate(notificationsProvider);
                              } on DioException catch (error) {
                                if (context.mounted) _message(context, error.message ?? 'Could not update notification.');
                              }
                            },
                            itemBuilder: (context) => const [
                              PopupMenuItem(value: 'read', child: Text('Mark as read')),
                              PopupMenuItem(value: 'archive', child: Text('Archive')),
                            ],
                          ),
                        ),
                      )).toList(),
                    ),
            ),
      );
}

class _Page extends StatelessWidget {
  const _Page({required this.title, required this.child, this.onRefresh});

  final String title;
  final Widget child;
  final Future<void> Function()? onRefresh;

  @override
  Widget build(BuildContext context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 20, 16, 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(child: Text(title, style: Theme.of(context).textTheme.headlineSmall)),
                  IconButton(
                    tooltip: 'Sign out',
                    onPressed: () async {
                      await _authController.signOut();
                    },
                    icon: const Icon(Icons.logout),
                  ),
                ],
              ),
              const SizedBox(height: 12),
              Expanded(
                child: onRefresh == null
                    ? child
                    : RefreshIndicator(onRefresh: onRefresh!, child: child),
              ),
            ],
          ),
        ),
      );
}

class _InfoCard extends StatelessWidget {
  const _InfoCard({required this.title, required this.value, required this.subtitle, required this.icon});

  final String title;
  final String value;
  final String subtitle;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Card(
        child: Padding(
          padding: const EdgeInsets.all(18),
          child: Row(
            children: [
              Icon(icon, size: 28, color: Theme.of(context).colorScheme.primary),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: Theme.of(context).textTheme.labelLarge),
                    const SizedBox(height: 4),
                    Text(value, style: Theme.of(context).textTheme.headlineSmall),
                    Text(subtitle, style: Theme.of(context).textTheme.bodySmall),
                  ],
                ),
              ),
            ],
          ),
        ),
      );
}

class _LoadError extends StatelessWidget {
  const _LoadError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) => Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 8),
            TextButton(onPressed: onRetry, child: const Text('Try again')),
          ],
        ),
      );
}

class _Empty extends StatelessWidget {
  const _Empty({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [Icon(icon, size: 40), const SizedBox(height: 8), Text(text)],
        ),
      );
}

String _money(dynamic amount) => NumberFormat.currency(
      locale: 'en_UG',
      symbol: 'UGX ',
      decimalDigits: 0,
    ).format((amount as num?) ?? 0);

String _date(dynamic value) {
  final parsed = DateTime.tryParse(value?.toString() ?? '');
  return parsed == null ? '—' : DateFormat('d MMM, HH:mm').format(parsed.toLocal());
}

void _message(BuildContext context, String message) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));
}
