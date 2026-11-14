using System.Collections.Generic;

namespace DashboardStarter.Api.Demo;

/// <summary>
/// What every demo sandbox is made of: two tenants, the visitor's role in each, the sample
/// members they see on the Members page, and each tenant's sample customers. <see cref="SandboxStore"/> creates one from this.
/// </summary>
/// <remarks>
/// <para>
/// The visitor is an Admin in "Demo" and Read-only in "Other Demo", so switching tenants shows
/// the same person with different permissions: controls in one tenant, none in the other.
/// </para>
/// <para>
/// The sample members are real users of this sandbox only, so the visitor can change their
/// roles and disable them. Users.Email is unique across the whole database, so each sandbox's
/// sample members get their own addresses, on the reserved .invalid domain (RFC 2606), which
/// can't belong to anyone. They have no password, so nobody can sign in as them.
/// </para>
/// <para>
/// Role names appear here because this is a definition, like the migration that creates the
/// roles. Code that decides what someone may do checks permissions only.
/// </para>
/// </remarks>
public static class SandboxDefinition
{
  /// <summary>The reserved domain every sandbox address is on. No real account can have one.</summary>
  public const string EmailDomain = "demo.invalid";

  public const string VisitorName = "Demo Guest";

  public static IReadOnlyList<string> Tenants { get; } = ["Demo", "Other Demo"];

  public static IReadOnlyList<SandboxMembership> Visitor { get; } =
  [
    new("Demo", "Admin", "Active"),
    new("Other Demo", "Read-only", "Active"),
  ];

  // Sam is an Admin in both, so "Other Demo" has someone who can manage members: the visitor
  // is Read-only there, and every tenant keeps an admin.
  public static IReadOnlyList<SandboxSampleMember> SampleMembers { get; } =
  [
    new("sam.rivera", "Sam Rivera", [new("Demo", "Admin", "Active"), new("Other Demo", "Admin", "Active")]),
    new("priya.shah", "Priya Shah", [new("Demo", "Member", "Active"), new("Other Demo", "Member", "Active")]),
    new("lee.chen", "Lee Chen", [new("Demo", "Read-only", "Active")]),
    new("jordan.blake", "Jordan Blake", [new("Demo", "Member", "Disabled")]),
  ];

  /// <summary>
  /// Sample customers per tenant: more than a page (25) in each, so paging shows, and different
  /// in each (businesses in Demo, people in Other Demo), so switching tenants shows they're
  /// separate. Emails are on the reserved .example domain and phone numbers in the 555-0100 to
  /// 555-0199 range set aside for fiction, so none of them reach anyone.
  /// </summary>
  public static IReadOnlyDictionary<string, IReadOnlyList<SandboxCustomer>> Customers { get; } =
    new Dictionary<string, IReadOnlyList<SandboxCustomer>>
    {
      ["Demo"] =
      [
        new("100% Natural Soap Co.", "hello@naturalsoap.example", "+1 555 0100", "Active", "The % in the name is a good test for search."),
        new("Alder & Finch Bakery", "orders@alderfinch.example", "+1 555 0101", "Active", "Weekly standing order, delivered Tuesdays."),
        new("Bluebird Logistics", "dispatch@bluebird.example", "+1 555 0102", "Active", null),
        new("Brightwater Dental", "frontdesk@brightwater.example", "+1 555 0103", "Active", null),
        new("Cascade Bike Works", "shop@cascadebikes.example", "+1 555 0104", "Active", "Asked about a bulk discount for team kits."),
        new("Cedar Lane Veterinary", "care@cedarlanevet.example", "+1 555 0105", "Active", null),
        new("Copperleaf Studio", "studio@copperleaf.example", null, "Inactive", "Closed for renovation until spring."),
        new("Driftwood Surf Shop", "aloha@driftwood.example", "+1 555 0107", "Active", null),
        new("Ember Coffee Roasters", "beans@ember.example", "+1 555 0108", "Active", null),
        new("Fernhill Garden Center", null, "+1 555 0109", "Active", "Prefers phone calls to email."),
        new("Foxglove Florists", "petals@foxglove.example", "+1 555 0110", "Active", null),
        new("Granite Peak Climbing Gym", "belay@granitepeak.example", "+1 555 0111", "Active", null),
        new("Harbor Light Books", "books@harborlight.example", "+1 555 0112", "Active", null),
        new("Hollow Oak Furniture", "workshop@hollowoak.example", "+1 555 0113", "Inactive", null),
        new("Ironwood Fitness", "team@ironwood.example", "+1 555 0114", "Active", null),
        new("Juniper Health Clinic", "admin@juniperhealth.example", "+1 555 0115", "Active", "Invoices go to the practice manager."),
        new("Kestrel Air Charter", "ops@kestrelair.example", "+1 555 0116", "Active", null),
        new("Lantern Theatre Company", "boxoffice@lantern.example", "+1 555 0117", "Active", null),
        new("Maple Street Hardware", "counter@maplestreet.example", "+1 555 0118", "Active", null),
        new("Meadowlark Farms", "farm@meadowlark.example", null, "Active", null),
        new("Northwind Lumber", "yard@northwind.example", "+1 555 0120", "Active", null),
        new("Orchard Hill Cider", "taproom@orchardhill.example", "+1 555 0121", "Active", null),
        new("Pinecone Daycare", "hello@pinecone.example", "+1 555 0122", "Active", null),
        new("Quarry Stone Masonry", "quotes@quarrystone.example", "+1 555 0123", "Inactive", null),
        new("Redwood Plumbing", "jobs@redwoodplumbing.example", "+1 555 0124", "Active", "Emergency line answers 24/7."),
        new("Riverbend Pharmacy", "rx@riverbend.example", "+1 555 0125", "Active", null),
        new("Saltmarsh Seafood", "catch@saltmarsh.example", "+1 555 0126", "Active", null),
        new("Silver Birch Yoga", "namaste@silverbirch.example", "+1 555 0127", "Active", null),
        new("Thistle & Thorn Tattoo", "ink@thistlethorn.example", "+1 555 0128", "Active", null),
        new("Willow Creek Kennels", "bark@willowcreek.example", "+1 555 0129", "Active", null),
      ],
      ["Other Demo"] =
      [
        new("Ada Okafor", "ada.okafor@mail.example", "+1 555 0130", "Active", null),
        new("Ana Lúcia Ferreira", "analucia@mail.example", "+1 555 0131", "Active", null),
        new("Bruno Silva", "bruno.silva@mail.example", null, "Active", null),
        new("Chen Wei", "chen.wei@mail.example", "+1 555 0133", "Active", "Prefers WeChat for quick questions."),
        new("Dalia Haddad", "dalia@mail.example", "+1 555 0134", "Active", null),
        new("Elias Novak", "elias.novak@mail.example", "+1 555 0135", "Inactive", "Moved abroad."),
        new("Farah Qureshi", "farah.q@mail.example", "+1 555 0136", "Active", null),
        new("Gustavo Reyes", "gustavo@mail.example", "+1 555 0137", "Active", null),
        new("Hana Kobayashi", "hana.k@mail.example", "+1 555 0138", "Active", null),
        new("Ivan Petrov", "ivan.petrov@mail.example", "+1 555 0139", "Active", null),
        new("Jana Horvat", "jana.horvat@mail.example", null, "Active", null),
        new("Kofi Mensah", "kofi@mail.example", "+1 555 0141", "Active", null),
        new("Lina Berg", "lina.berg@mail.example", "+1 555 0142", "Active", null),
        new("Mateo Rossi", "mateo.rossi@mail.example", "+1 555 0143", "Active", null),
        new("Nadia Karim", "nadia.karim@mail.example", "+1 555 0144", "Active", null),
        new("Oskar Lind", "oskar@mail.example", "+1 555 0145", "Inactive", null),
        new("Paula Mendes", "paula.mendes@mail.example", "+1 555 0146", "Active", null),
        new("Quinn O'Brien", "quinn.obrien@mail.example", "+1 555 0147", "Active", "Shares an inbox with Tess Morgan."),
        new("Rosa Jiménez", "rosa.jimenez@mail.example", "+1 555 0148", "Active", null),
        new("Samir Aziz", "samir.aziz@mail.example", "+1 555 0149", "Active", null),
        new("Tess Morgan", "quinn.obrien@mail.example", "+1 555 0150", "Active", "Same address as Quinn O'Brien: customer email isn't unique."),
        new("Uma Patel", "uma.patel@mail.example", "+1 555 0151", "Active", null),
        new("Viktor Hansen", "viktor@mail.example", "+1 555 0152", "Active", null),
        new("Wren Adams", "wren.adams@mail.example", "+1 555 0153", "Active", null),
        new("Ximena Cruz", "ximena.cruz@mail.example", "+1 555 0154", "Active", null),
        new("Yusuf Demir", "yusuf.demir@mail.example", "+1 555 0155", "Active", null),
        new("Zara Ahmed", "zara.ahmed@mail.example", "+1 555 0156", "Active", null),
      ],
    };
}

public sealed record SandboxMembership(string TenantName, string RoleName, string Status);

/// <param name="EmailName">The part of the address before the sandbox's own suffix: sam.rivera-1a2b3c4d5e6f@demo.invalid.</param>
public sealed record SandboxSampleMember(string EmailName, string DisplayName, IReadOnlyList<SandboxMembership> Memberships);

public sealed record SandboxCustomer(string Name, string? Email, string? Phone, string Status, string? Notes);
