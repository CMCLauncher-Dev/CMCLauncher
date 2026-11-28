#include "cubiomes/finders.h"
#include "cubiomes/util.h"

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

static const int versions[] = {
    MC_1_13_2, MC_1_14_4, MC_1_15_2, MC_1_16_1, MC_1_16_5, MC_1_17_1,
    MC_1_18_2, MC_1_19_2, MC_1_19_4, MC_1_20_6, MC_1_21_1, MC_1_21_3, MC_1_21_WD
};
static const int structures[] = {
    Village, Outpost, Desert_Pyramid, Jungle_Temple, Swamp_Hut, Igloo,
    Monument, Mansion, Ancient_City, Shipwreck, Ocean_Ruin, Ruined_Portal,
    Trail_Ruins, Trial_Chambers, -1, Fortress, Bastion, Ruined_Portal_N, End_City, End_Gateway
};

static int floor_div(int value, int divisor)
{
    return value / divisor - (value % divisor < 0);
}

static int inside(Pos p, int x, int z, int span_x, int span_z)
{
    return p.x >= x && p.z >= z && p.x < x + span_x && p.z < z + span_z;
}

typedef struct { int x, z, type; } MapStructure;

static int find_structures(Generator *generator, int mc, uint64_t seed, int x, int z,
    int span_x, int span_z, int type, MapStructure *positions, int capacity)
{
    int count = 0;
    if (type == -2)
    {
        if (generator->dim != DIM_OVERWORLD)
            return 0;
        StrongholdIter iter;
        initFirstStronghold(&iter, mc, seed);
        while (count < capacity && nextStronghold(&iter, generator))
        {
            if (inside(iter.pos, x, z, span_x, span_z))
                positions[count++] = (MapStructure){iter.pos.x, iter.pos.z, 1};
            if (iter.index == 128)
                break;
        }
        return count;
    }
    if (type < 0 || type >= sizeof(structures)/sizeof(structures[0]))
        return 0;

    int structure = structures[type];
    StructureConfig config;
    if (structure < 0 || !getStructureConfig(structure, mc, &config) || config.dim != generator->dim)
        return 0;
    SurfaceNoise end_noise;
    if (structure == End_City)
        initSurfaceNoise(&end_noise, DIM_END, seed);
    int region = config.regionSize*16;
    int max_x = floor_div(x + span_x - 1, region);
    int max_z = floor_div(z + span_z - 1, region);
    for (int rz = floor_div(z, region); rz <= max_z && count < capacity; rz++)
    {
        for (int rx = floor_div(x, region); rx <= max_x && count < capacity; rx++)
        {
            Pos p;
            if (getStructurePos(structure, mc, seed, rx, rz, &p)
                && inside(p, x, z, span_x, span_z)
                && isViableStructurePos(structure, generator, p.x, p.z, 0)
                && (generator->dim != DIM_OVERWORLD || mc < MC_1_18
                    || isViableStructureTerrain(structure, generator, p.x, p.z))
                && (structure != End_City
                    || isViableEndCityTerrain(generator, &end_noise, p.x, p.z)))
                positions[count++] = (MapStructure){p.x, p.z, type + 2};
        }
    }
    return count;
}

EXPORT int cmc_seed_render(int version, uint64_t seed, int x, int z, int step, int width, int height, int dimension, int type,
    unsigned char *pixels, unsigned char *biomes, MapStructure *positions, int capacity)
{
    if (version < 0 || version >= sizeof(versions)/sizeof(versions[0]))
        return -1;
    int mc = versions[version];
    Generator generator;
    setupGenerator(&generator, mc, 0);
    applySeed(&generator, dimension, seed);
    unsigned char colors[256][3];
    initBiomeColors(colors);
    int biome_scale = dimension == DIM_END ? 16 : 4;
    Range sample = {biome_scale, 0, 0, 1, 1, 64 / biome_scale, 1};
    int *cache = allocCache(&generator, sample);
    if (!cache)
        return -1;
    int previous_z = 0;
    for (int row = 0; row < height; row++)
    {
        sample.z = floor_div(z + row*step + step/2, biome_scale);
        if (row > 0 && sample.z == previous_z)
        {
            memcpy(biomes + row*width, biomes + (row - 1)*width, width);
            memcpy(pixels + 4*row*width, pixels + 4*(row - 1)*width, 4*width);
            continue;
        }
        previous_z = sample.z;
        int previous_x = 0, id = 0;
        for (int col = 0; col < width; col++)
        {
            sample.x = floor_div(x + col*step + step/2, biome_scale);
            if (col == 0 || sample.x != previous_x)
            {
                if (genBiomes(&generator, cache, sample) != 0 || cache[0] < 0 || cache[0] >= 256)
                {
                    free(cache);
                    return -1;
                }
                id = cache[0];
                previous_x = sample.x;
            }
            int pixel = row*width + col;
            biomes[pixel] = (unsigned char)id;
            pixels[4*pixel] = colors[id][2];
            pixels[4*pixel+1] = colors[id][1];
            pixels[4*pixel+2] = colors[id][0];
            pixels[4*pixel+3] = 255;
        }
    }
    free(cache);

    int span_x = width*step, span_z = height*step;
    if (type != -3)
        return find_structures(&generator, mc, seed, x, z, span_x, span_z, type, positions, capacity);
    int count = find_structures(&generator, mc, seed, x, z, span_x, span_z, -2, positions, capacity);
    for (int i = 0; i < sizeof(structures)/sizeof(structures[0]) && count < capacity; i++)
        count += find_structures(&generator, mc, seed, x, z, span_x, span_z, i, positions + count, capacity - count);
    return count;
}

EXPORT const char *cmc_seed_biome_name(int version, int id)
{
    if (version < 0 || version >= sizeof(versions)/sizeof(versions[0]))
        return 0;
    return biome2str(versions[version], id);
}
